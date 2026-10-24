// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AppleRemote.App.Models;
using AppleRemote.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AppleRemote.App.ViewModels;

public enum StatusLevel { Idle, Ok, Warn }

/// <summary>One finger on the touch surface, in pixels of the 360 px square (top-left corner of the dot).</summary>
public sealed partial class FingerViewModel(int index) : ObservableObject
{
    public int Index { get; } = index;
    [ObservableProperty] bool _isVisible;
    [ObservableProperty] double _left;
    [ObservableProperty] double _top;
}

/// <summary>Remote page (and the remote drawing of the other pages): connection status, live buttons, touch and gestures.</summary>
public sealed partial class RemoteViewModel : ObservableObject, IDisposable
{
    // Observed touch range (≈ 1650–1800 units per axis, not centred on 0) -> 360 px square.
    const double PadSize = 360, XMin = -1900, YMin = -1150, Span = 1800, DotSize = 30;
    const long TrailLifeMs = 1000;

    readonly RemoteSession _session;
    readonly Action<string> _log;
    readonly Action<string> _runBound;
    readonly Dispatcher _ui = Dispatcher.CurrentDispatcher;
    readonly List<(Point Point, long Time)> _trail = new();
    readonly A2854VisualContactTracker _a2854ContactTracker = new();
    readonly List<(Point Point, long Time)>[] _a2854Trails = [new(), new()];
    readonly BleScanner _scanner = new();
    readonly IDialogs _dialogs;
    readonly DispatcherTimer _scanTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer _trailTimer = new() { Interval = TimeSpan.FromMilliseconds(30) };
    readonly DispatcherTimer _gestureTimer = new() { Interval = TimeSpan.FromMilliseconds(TrailLifeMs) };
    bool _wasDown;
    readonly A2854TouchSampleWindow _calibrationWindow = new();
    bool _calibrationFingerDown;
    readonly Dictionary<string, A2854TouchReference> _touchReferences =
        new(StringComparer.Ordinal);
    A2854TouchCalibration? _calibratedProjection;
    static readonly string TouchCalibrationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AppleRemote", "A2854-touch-calibration.json");
    public string[] CalibrationTargets { get; } = ["center", "left", "right", "top", "bottom"];
    [ObservableProperty] string _selectedCalibrationTarget = "center";
    [ObservableProperty] string _touchCalibrationSummary = "0 / 5 reference points collected";
    [ObservableProperty] string _liveTouchCoordinates = "No active touch";
    [ObservableProperty] bool _touchCalibrationPreviewEnabled;
    public string TouchPreviewActionText => TouchCalibrationPreviewEnabled
        ? "Compare raw" : "Use saved calibration";
    public string TouchDisplayModeText => _calibratedProjection is null
        ? "No valid five-point profile. Raw display in use."
        : TouchCalibrationPreviewEnabled
            ? "Measured calibration active (display only)"
            : "Raw signed-12-bit comparison active";
    partial void OnTouchCalibrationPreviewEnabledChanged(bool value)
    {
        OnPropertyChanged(nameof(TouchPreviewActionText));
        OnPropertyChanged(nameof(TouchDisplayModeText));
    }
    // A queued Dispatcher callback must never display data from a previous
    // connection or remote model after Start/Stop/model switching.
    long _visualEpoch;

    /// <summary>BLE devices seen advertising with Apple manufacturer data + HID service ("Apple HID").</summary>
    [ObservableProperty] IReadOnlyList<NearbyRow> _nearbyDevices = [];
    [ObservableProperty] NearbyRow? _selectedNearby;

    /// <summary>Pairing started from the nearby list: spinner while <see cref="IsPairing"/>, then the result for a few seconds.</summary>
    [ObservableProperty] bool _isPairing;
    [ObservableProperty] string _pairText = "";

    /// <summary>A row of the nearby list; <see cref="Connected"/> = seen by Windows' HID, not over BLE.</summary>
    public sealed record NearbyRow(string Name, string Address, string Signal, bool Connected, ulong Key = 0);

    /// <summary>Settings, for the adapter choice shown on the Remote page.</summary>
    public SettingsViewModel? Preferences { get; set; }

    public FingerViewModel[] Fingers { get; } = [new(0), new(1)];

    [ObservableProperty] string _statusText = Loc.T("Status.Stopped");

    // Generated only from our own status strings; never store peer addresses,
    // raw HCI/ATT frames, cryptographic keys or the protected bond in the UI.
    [ObservableProperty] string _a2854LastLinkSummary = "No A2854 session result yet.";
    readonly A2854SessionTimeline _sessionTimeline = new();
    [ObservableProperty] string _a2854TimelineCount = "0 diagnostic events";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusTitle))]
    StatusLevel _statusLevel;

    public string StatusTitle => Loc.T("Remote.Level." + StatusLevel);
    [ObservableProperty] PointCollection _trailPoints = new();
    [ObservableProperty] PointCollection _a2854TrailOnePoints = new();
    [ObservableProperty] PointCollection _a2854TrailTwoPoints = new();
    [ObservableProperty] double _tileShiftX;
    [ObservableProperty] double _tileShiftY;
    [ObservableProperty] string _moveArrow = "·";
    [ObservableProperty] string _gestureText = "—";

    [ObservableProperty] bool _menuPressed;
    [ObservableProperty] bool _tvPressed;
    [ObservableProperty] bool _siriPressed;
    [ObservableProperty] bool _playPressed;
    [ObservableProperty] bool _volUpPressed;
    [ObservableProperty] bool _volDownPressed;
    [ObservableProperty] bool _touchPressed;

    // A2854 has physical mute, power, and a four-way clickpad ring.
    // Legacy mode keeps the old drawing and button state unchanged.
    [ObservableProperty] bool _mutePressed;
    [ObservableProperty] bool _powerPressed;
    [ObservableProperty] bool _upPressed;
    [ObservableProperty] bool _downPressed;
    [ObservableProperty] bool _leftPressed;
    [ObservableProperty] bool _rightPressed;

    public bool IsA2854Mode => _session.A2854Mode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BatteryText))]
    [NotifyPropertyChangedFor(nameof(BatteryPercent))]
    [NotifyPropertyChangedFor(nameof(BatteryGlyph))]
    int? _batteryLevel;

    /// <summary>Segoe MDL2 battery glyph in steps of 10 % (Battery0 = E850 … Battery9 = E858, Battery10 = E83F); E996 = unknown.</summary>
    public string BatteryGlyph => BatteryLevel is { } level
        ? char.ConvertFromUtf32(Math.Clamp((level + 5) / 10, 0, 10) switch { 10 => 0xE83F, var step => 0xE850 + step })
        : "";

    public string BatteryPercent => BatteryLevel is { } level ? $"{level} %" : "—";

    /// <summary>Last known level (direct Bluetooth); "—" until a remote has reported one.</summary>
    public string BatteryText => BatteryLevel is { } level ? Loc.T("Remote.Battery", level) : Loc.T("Remote.BatteryUnknown");

    public string StartStopText => Loc.T(
        _session.CanRestartEndedA2854 ? "Remote.Reconnect" :
        _session.IsRunning ? "Remote.Stop" : "Remote.Start");

    /// <param name="runBound">Runs what is assigned to a remote input (key, software, combo, macro).</param>
    public RemoteViewModel(RemoteSession session, IDialogs dialogs, Action<string> log, Action<string> runBound)
    {
        _session = session;
        _dialogs = dialogs;
        _log = log;
        _runBound = runBound;
        LoadTouchCalibrationPoints();
        _session.Event += ev =>
        {
            long epoch = Interlocked.Read(ref _visualEpoch);
            _ = _ui.InvokeAsync(() =>
            {
                if (epoch == Interlocked.Read(ref _visualEpoch) && _session.IsRunning)
                    OnEvent(ev);
            }, DispatcherPriority.Render);
        };
        _session.Gesture += g =>
        {
            long epoch = Interlocked.Read(ref _visualEpoch);
            _ = _ui.InvokeAsync(() =>
            {
                if (epoch == Interlocked.Read(ref _visualEpoch) && _session.IsRunning)
                    OnGesture(g);
            }, DispatcherPriority.Render);
        };
        _session.Status += status =>
        {
            long epoch = Interlocked.Read(ref _visualEpoch);
            _ = _ui.InvokeAsync(() =>
            {
                if (epoch == Interlocked.Read(ref _visualEpoch))
                    OnStatus(status);
            });
        };
        _session.Battery += level =>
        {
            long epoch = Interlocked.Read(ref _visualEpoch);
            _ = _ui.InvokeAsync(() =>
            {
                if (epoch == Interlocked.Read(ref _visualEpoch) && _session.IsRunning)
                    BatteryLevel = level;
            });
        };

        _trailTimer.Tick += (_, _) => FadeTrail();
        _gestureTimer.Tick += (_, _) => ResetGesture();
        _scanTimer.Tick += async (_, _) =>
        {
            // Native A2854 finishes USB cleanup asynchronously after the peer
            // disconnects. Refresh its manual Reconnect action when ready.
            OnPropertyChanged(nameof(StartStopText));
            var all = await Task.Run(() =>
            {
                try { return HidEnumerator.ListAll(); }
                catch { return []; }
            });
            // The dongle is only picked when the session starts: one plugged in later (the app runs at logon, the dongle
            // reboots after a cable flash) takes over the running session.
            if (RemoteStartupPolicy.AllowBackgroundRestart(_session.A2854Mode) &&
                _session.IsRunning && !_session.UsesDongle && all.Any(EspDongle.IsDongle))
            {
                _log(Loc.T("Log.DongleDetected"));
                StopSession();
                StartSession();
            }
            var hid = all.Where(HidEnumerator.IsApple);
            // A single remote exposes several HID collections: one line per product.
            var connected = hid.DistinctBy(d => (d.VendorId, d.ProductId))
                .Select(d => new NearbyRow(d.IsSiriRemoteGen1 ? "Siri Remote (1st)" : d.Product.Length > 0 ? d.Product : $"HID {d.VendorId:X4}:{d.ProductId:X4}",
                    "", Loc.T("Remote.Connected"), true));
            // Direct Bluetooth or dongle: the remote is connected through them, invisible to Windows (no HID, no advertising).
            if ((_session.UsesBluetoothDirect || _session.UsesDongle) && StatusLevel == StatusLevel.Ok)
                connected = connected.Append(new NearbyRow(
                    _session.A2854Mode ? "Siri Remote A2854 (USB-C)" : "Siri Remote (1st)",
                    "", Loc.T("Remote.Connected"), true));
            var nearby = _scanner.Snapshot().Where(d => d.AppleHid)
                .Select(d => new NearbyRow(_session.A2854Mode ? "Apple HID (unpaired; A2854 mode uses saved bond)" :
                    "Siri Remote (1st)", d.AddressText, $"{d.Rssi} dBm", false, d.Address));
            // The list is rebuilt on every tick: the selection is kept by name + address.
            var selected = SelectedNearby;
            NearbyDevices = connected.Concat(nearby).ToList();
            SelectedNearby = NearbyDevices.FirstOrDefault(r => r.Name == selected?.Name && r.Address == selected.Address);
        };
    }

    public void Start()
    {
        // The existing app starts first-generation remotes automatically.
        // A2854 can write 0xFB/0xFC CCCDs and feature 0xF0 immediately after
        // connecting, so saved A2854 mode requires an explicit Start click.
        // A legacy auto-start could select the external A2854 Barrot as the
        // first generic WinUSB adapter while A2854Mode is false. A read-only
        // interface lookup prevents any implicit HCI connection in that case.
        // Enumeration errors also fail closed: Start remains available in UI.
        bool dedicatedBarrotPresent = _session.A2854Mode;
        if (!dedicatedBarrotPresent)
        {
            try
            {
                dedicatedBarrotPresent = UsbHci.IsWinUsbDevicePresent(new UsbHci.UsbId(0x33FA, 0x0001));
            }
            catch (Exception e)
            {
                dedicatedBarrotPresent = true;
                _log($"Remote: WinUSB device enumeration unavailable ({e.GetType().Name}); auto-start disabled.");
            }
        }
        if (RemoteStartupPolicy.AutoStartSession(_session.A2854Mode, dedicatedBarrotPresent))
        {
            StartSession();
            _scanner.Start();
        }
        // A2854 or dedicated-Barrot startup is passive: no direct HCI,
        // no WinRT BLE scanner, no GATT writes or user-input synthesis.
        // Explicit Start is the only route to native A2854 transport.
        _scanTimer.Start();
    }

    /// <summary>
    /// Switching remote models while stopped updates only Windows BLE scanning.
    /// A2854 relies on its stored DPAPI bond and must never be paired via
    /// Windows nearby-device lists; this does not start a remote session.
    /// </summary>
    public void OnRemoteModeChanged(bool a2854Mode)
    {
        Interlocked.Increment(ref _visualEpoch);
        if (a2854Mode) _scanner.Stop();
        else if (_scanTimer.IsEnabled) _scanner.Start();
        ClearNearby();
        // The Remote page switches its physical illustration at runtime.
        // A preceding model's button/touch highlights must not leak through.
        OnPropertyChanged(nameof(IsA2854Mode));
        BatteryLevel = null;
        A2854LastLinkSummary = "No A2854 session result yet.";
        StatusText = Loc.T("Status.Stopped");
        StatusLevel = StatusLevel.Idle;
        ShowButtons(0);
        foreach (var finger in Fingers) finger.IsVisible = false;
        _trail.Clear();
        TrailPoints = new PointCollection();
        _trailTimer.Stop();
        _wasDown = false;
        ClearA2854VisualContacts();
        _calibrationWindow.Clear();
        _calibrationFingerDown = false;
        LiveTouchCoordinates = "No active touch";
        // Valid saved physical landmarks auto-apply when returning to A2854.
        // Switching to first generation never inherits the A2854 projection.
        TouchCalibrationPreviewEnabled = a2854Mode && _calibratedProjection is not null;
        ResetGesture();
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanner.Dispose();
    }

    /// <summary>The language changed: texts computed here are re-read.</summary>
    public void RefreshTexts() => OnPropertyChanged(string.Empty);

    /// <summary>Empties the nearby list (adapters changed, new session): devices seen before are no longer meaningful.</summary>
    public void ClearNearby()
    {
        _scanner.Clear();
        NearbyDevices = [];
    }

    public void StartSession()
    {
        Interlocked.Increment(ref _visualEpoch);
        ClearA2854VisualContacts();
        _calibrationWindow.Clear();
        _calibrationFingerDown = false;
        LiveTouchCoordinates = "No active touch";
        if (_session.A2854Mode)
        {
            A2854LastLinkSummary = "Connecting to remote with existing protected bond…";
            _sessionTimeline.Reset();
            A2854TimelineCount = "0 diagnostic events";
        }
        ClearNearby();
        _session.Start();
        // If startup was deliberately passive because Barrot was present,
        // an explicit legacy Start still enables the old Windows BLE scanner.
        // Native A2854 never depends on Windows' nearby-device scanning.
        if (!_session.A2854Mode && _scanTimer.IsEnabled)
            _scanner.Start();
        RefreshButtons();
    }

    public void StopSession()
    {
        // Invalidate callbacks that were queued before stopping the session.
        Interlocked.Increment(ref _visualEpoch);
        _session.Stop();
        ShowButtons(0);
        foreach (var finger in Fingers) finger.IsVisible = false;
        _trail.Clear();
        TrailPoints = new PointCollection();
        _trailTimer.Stop();
        _wasDown = false;
        ClearA2854VisualContacts();
        _calibrationWindow.Clear();
        _calibrationFingerDown = false;
        LiveTouchCoordinates = "No active touch";
        RefreshButtons();
    }

    // ---- remote events ---------------------------------------------------

    void OnEvent(RemoteEvent ev)
    {
        switch (ev)
        {
            case TouchEvent t:
                ShowTouch(t);
                break;
            case ButtonsEvent b:
                ShowButtons(b.Mask);
                foreach (var name in b.Pressed)
                    if (!(_session.A2854Mode &&
                        (name is "tv" or "siri" or "power" ||
                        (_session.A2854PointerEnabled && name == "click"))))
                        _runBound(name);
                if (b.Pressed.Count > 0) _log("▼ " + string.Join(" + ", b.Pressed));
                if (b.Released.Count > 0) _log("▲ " + string.Join(" + ", b.Released));
                break;
            case VoiceChunkEvent:
                break; // audio: not decodable over HID, ignored
        }
    }

    // 1st-generation 0xFC and A2854 0xFB have unrelated button masks.
    // Keep both models' visuals separate, including A2854-only keys.
    void ShowButtons(int mask)
    {
        if (_session.A2854Mode)
        {
            var buttons = A2854ButtonVisualState.FromMask(mask);
            MenuPressed = buttons.Back;
            TvPressed = buttons.Tv;
            SiriPressed = buttons.Siri;
            PlayPressed = buttons.PlayPause;
            VolUpPressed = buttons.VolumeUp;
            VolDownPressed = buttons.VolumeDown;
            TouchPressed = buttons.CenterClick;
            MutePressed = buttons.Mute;
            PowerPressed = buttons.Power;
            UpPressed = buttons.Up;
            RightPressed = buttons.Right;
            DownPressed = buttons.Down;
            LeftPressed = buttons.Left;
            return;
        }
        MenuPressed = (mask & 0x20) != 0;
        TvPressed = (mask & 0x01) != 0;
        SiriPressed = (mask & 0x10) != 0;
        PlayPressed = (mask & 0x08) != 0;
        VolUpPressed = (mask & 0x02) != 0;
        VolDownPressed = (mask & 0x04) != 0;
        TouchPressed = (mask & 0x80) != 0;
        MutePressed = PowerPressed = UpPressed = RightPressed = DownPressed = LeftPressed = false;
    }

    void ShowTouch(TouchEvent t)
    {
        if (_session.A2854Mode)
        {
            if (t.Down && t.Fingers.Count == 1 &&
                t.Fingers[0].State is >= 3 and <= 6)
            {
                // A new contact clears previous positions. Only an active
                // physical touch may contribute to the selected reference.
                if (!_calibrationFingerDown) _calibrationWindow.Clear();
                _calibrationFingerDown = true;
                _calibrationWindow.Add(t.X, t.Y, Environment.TickCount64);
                LiveTouchCoordinates = $"Live 0xFC: X={t.X}, Y={t.Y}";
            }
            else
            {
                _calibrationFingerDown = false;
                _calibrationWindow.Clear();
                LiveTouchCoordinates = "No active touch";
            }
        }
        if (_session.A2854Mode)
        {
            ShowA2854Contacts(t);
            return;
        }

        bool anyDown = false;
        for (int i = 0; i < Fingers.Length; i++)
        {
            bool visible = i < t.Fingers.Count && t.Fingers[i].State is >= 3 and <= 6;
            Fingers[i].IsVisible = visible;
            if (!visible) continue;
            anyDown = true;
            var f = t.Fingers[i];
            var position = ProjectTouch(f.X, f.Y);
            Fingers[i].Left = position.X - DotSize / 2;
            Fingers[i].Top = position.Y - DotSize / 2;
        }

        if (anyDown)
        {
            if (!_wasDown) _trail.Clear();
            var primary = ProjectTouch(t.X, t.Y);
            _trail.Add((new Point(primary.X, primary.Y), Environment.TickCount64));
            TrailPoints = new PointCollection(_trail.Select(p => p.Point));
            _trailTimer.Start();
        }
        _wasDown = anyDown;
    }

    /// <summary>
    /// A2854 contacts lack persistent IDs in TouchEvent.Fingers and decoder
    /// packet-slot ordering is not a finger identity. Keep two independent
    /// markers/trails by nearest-position matching. Legacy rendering above
    /// retains its existing single-primary-contact semantics.
    /// </summary>
    void ShowA2854Contacts(TouchEvent t)
    {
        List<A2854VisualContact> contacts = [];
        if (t.Down)
            foreach (var finger in t.Fingers)
                if (finger.State is >= 3 and <= 6)
                    contacts.Add(new A2854VisualContact(finger.X, finger.Y));

        var previous = _a2854ContactTracker.Slots;
        var assigned = _a2854ContactTracker.Update(contacts);
        long now = Environment.TickCount64;
        for (int i = 0; i < Fingers.Length; i++)
        {
            if (assigned[i] is not { } contact)
            {
                Fingers[i].IsVisible = false;
                _a2854Trails[i].Clear();
                continue;
            }
            var pos = ProjectTouch(contact.X, contact.Y);
            Fingers[i].Left = pos.X - DotSize / 2;
            Fingers[i].Top = pos.Y - DotSize / 2;
            Fingers[i].IsVisible = true;
            if (previous[i] is null) _a2854Trails[i].Clear();
            _a2854Trails[i].Add((new Point(pos.X, pos.Y), now));
        }
        A2854TrailOnePoints = new PointCollection(_a2854Trails[0].Select(p => p.Point));
        A2854TrailTwoPoints = new PointCollection(_a2854Trails[1].Select(p => p.Point));
        if (_a2854Trails.Any(x => x.Count > 0)) _trailTimer.Start();
        else if (_trail.Count == 0) _trailTimer.Stop();
    }

    void ClearA2854VisualContacts()
    {
        _a2854ContactTracker.Reset();
        foreach (var finger in Fingers) finger.IsVisible = false;
        foreach (var trail in _a2854Trails) trail.Clear();
        A2854TrailOnePoints = new PointCollection();
        A2854TrailTwoPoints = new PointCollection();
    }

    /// <summary>Each finger's trail fades independently; no cross-finger line.</summary>
    void FadeTrail()
    {
        long limit = Environment.TickCount64 - TrailLifeMs;
        if (TrimTrail(_trail, limit))
            TrailPoints = new PointCollection(_trail.Select(p => p.Point));
        if (TrimTrail(_a2854Trails[0], limit))
            A2854TrailOnePoints = new PointCollection(_a2854Trails[0].Select(p => p.Point));
        if (TrimTrail(_a2854Trails[1], limit))
            A2854TrailTwoPoints = new PointCollection(_a2854Trails[1].Select(p => p.Point));
        if (_trail.Count == 0 && _a2854Trails.All(x => x.Count == 0))
            _trailTimer.Stop();
    }

    static bool TrimTrail(List<(Point Point, long Time)> trail, long limit)
    {
        int firstRecent = trail.FindIndex(p => p.Time > limit);
        if (firstRecent < 0) firstRecent = trail.Count;
        if (firstRecent == 0) return false;
        trail.RemoveRange(0, firstRecent);
        return true;
    }

    // The existing first-gen range is retained only for first-gen input.
    // A2854 gets an independent signed-12-bit, round-pad display projection.
    (double X, double Y) ProjectTouch(int x, int y) => _session.A2854Mode
        ? TouchCalibrationPreviewEnabled && _calibratedProjection is { } profile
            ? profile.Map(x, y)
            : A2854TouchProjection.Map(x, y)
        : (PadX(x), PadY(y));

    static double PadX(int x) => Math.Clamp((x - XMin) / Span, 0, 1) * PadSize;

    static double PadY(int y) => (1 - Math.Clamp((y - YMin) / Span, 0, 1)) * PadSize;

    /// <summary>Gesture card back to idle: arrow, text and tilt shift.</summary>
    void ResetGesture()
    {
        _gestureTimer.Stop();
        MoveArrow = "·";
        GestureText = "—";
        TileShiftX = TileShiftY = 0;
    }

    void OnGesture(GestureEvent g)
    {
        _gestureTimer.Stop();
        _gestureTimer.Start();   // the card fades with the trail: idle TrailLifeMs after the last gesture
        switch (g)
        {
            case TiltGesture tilt:
                TileShiftX = tilt.X * 18;
                TileShiftY = -tilt.Y * 18;
                break;
            case MoveGesture m:
                _runBound(m.Dir.ToString().ToLowerInvariant());
                MoveArrow = m.Dir switch
                {
                    Direction.Up => "↑",
                    Direction.Down => "↓",
                    Direction.Left => "←",
                    _ => "→",
                };
                string src = Loc.T(m.Src == MoveSource.Flick ? "Src.Flick" : "Src.Glide");
                string dir = Loc.T("Dir." + m.Dir);
                GestureText = $"{dir} ({src})";
                _log(Loc.T("Log.Focus", dir, src));
                break;
            case TouchGesture { Down: true }:
                TileShiftX = TileShiftY = 0;
                break;
        }
    }

    void OnStatus(string message)
    {
        // Battery is optional: its failure is logged but must not hide the connection status.
        if (Loc.StartsWith(message, "Status.BatteryUnreadable")) { _log(message); return; }
        StatusText = message;
        if (_session.A2854Mode && _sessionTimeline.Record(message))
            A2854TimelineCount = $"{_sessionTimeline.Snapshot().Count} diagnostic events (max 48)";
        if (_session.A2854Mode && IsA2854OutcomeMessage(message))
        {
            // Copy result and the on-screen outcome use exactly the same
            // allowlisted redaction as Copy timeline. Never cache/export raw
            // callback messages with an untrusted suffix or a device address.
            A2854LastLinkSummary = A2854SessionTimeline.Redact(message)
                ?? "Connection event (details withheld)";
        }
        bool ok = new[] { "Status.ConnectedHid", "Status.ConnectedDirect", "Status.ConnectedDongle" }.Any(k => Loc.StartsWith(message, k));
        bool warn = new[] { "Status.WaitHid", "Status.WaitBonded", "Status.WaitPairing", "Status.Unreachable", "Status.Disconnected", "Status.DisconnectedReason", "Status.WaitDongle", "Status.DongleIdle" }
            .Any(k => Loc.StartsWith(message, k));
        // "Stopped" only if the session is really stopped: an unknown message keeps the current level.
        StatusLevel = ok ? StatusLevel.Ok : warn ? StatusLevel.Warn : _session.IsRunning ? StatusLevel : StatusLevel.Idle;
        _log(message);
    }

    static bool IsA2854OutcomeMessage(string message) =>
        message.StartsWith("A2854: peer disconnection ", StringComparison.Ordinal) ||
        message.StartsWith("A2854: HCI error phase=", StringComparison.Ordinal) ||
        message.StartsWith("A2854: session error phase=", StringComparison.Ordinal) ||
        message.StartsWith("A2854: early ATT request count=", StringComparison.Ordinal) ||
        message.StartsWith("A2854: host disconnect completed", StringComparison.Ordinal) ||
        message.StartsWith("A2854: disconnect ended with", StringComparison.Ordinal) ||
        message.StartsWith("A2854: connection or operation timed out.", StringComparison.Ordinal) ||
        message.StartsWith("A2854: dedicated Barrot WinUSB interface unavailable", StringComparison.Ordinal) ||
        message.StartsWith("A2854: previous USB", StringComparison.Ordinal) ||
        message.StartsWith("A2854: transport error (", StringComparison.Ordinal) ||
        message.StartsWith("A2854: link cleanup incomplete", StringComparison.Ordinal);

    /// <summary>
    /// Raw 0xFC geometry is stored apart from settings and the protected bond.
    /// Only a complete validated five-point profile auto-applies to the
    /// on-screen A2854 marker, never to the actual Windows mouse.
    /// </summary>
    void LoadTouchCalibrationPoints()
    {
        try
        {
            if (!File.Exists(TouchCalibrationPath)) return;
            var saved = A2854TouchCalibrationStore.Load(TouchCalibrationPath);
            foreach (var (name, point) in saved)
                _touchReferences[name] = point;
            RefreshTouchCalibration();
        }
        catch (Exception)
        {
            TouchCalibrationSummary = "Could not load calibration points; no preview applied.";
        }
    }

    void RefreshTouchCalibration()
    {
        _calibratedProjection = A2854TouchCalibration.TryCreate(
            _touchReferences, out var calibrated) ? calibrated : null;
        TouchCalibrationSummary =
            $"Saved {_touchReferences.Count} / 5 references: " +
            string.Join(", ", CalibrationTargets.Select(name =>
                _touchReferences.TryGetValue(name, out var point)
                    ? $"{name} ({point.X}, {point.Y})" : $"{name} (—)")) +
            (_calibratedProjection is null ? " — preview unavailable until valid." :
                " — ready for optional visual preview.");
        // A complete valid user-measured profile is the display default.
        // The Toggle preview control still allows a temporary comparison with
        // the original raw signed-12-bit fallback, but never affects SendInput.
        TouchCalibrationPreviewEnabled = _session.A2854Mode && _calibratedProjection is not null;
    }

    [RelayCommand]
    void CaptureCalibrationPoint()
    {
        if (!_session.A2854Mode)
        {
            TouchCalibrationSummary = "Select A2854 mode before recording touch geometry.";
            return;
        }
        if (!_calibrationFingerDown ||
            !_calibrationWindow.TryReadStable(Environment.TickCount64, out var point))
        {
            TouchCalibrationSummary =
                "Keep one finger on the chosen landmark, steady for at least 0.1 s; " +
                "moving or released touches are not captured.";
            return;
        }
        if (!CalibrationTargets.Contains(SelectedCalibrationTarget)) return;
        // Persist across the device's observed ~30s disconnects. This file
        // contains only small coordinate pairs, never BLE IDs/keys/reports.
        try
        {
            var updated = new Dictionary<string, A2854TouchReference>(
                _touchReferences, StringComparer.Ordinal)
                { [SelectedCalibrationTarget] = point };
            A2854TouchCalibrationStore.Save(TouchCalibrationPath, updated);
            _touchReferences[SelectedCalibrationTarget] = point;
            RefreshTouchCalibration();
        }
        catch (Exception e)
        {
            TouchCalibrationSummary = $"Could not save reference ({e.GetType().Name}); previous points preserved.";
        }
    }

    /// <summary>Copy a bounded, strictly whitelisted A2854 event timeline.</summary>
    [RelayCommand]
    void CopyA2854SessionTimeline()
    {
        if (!_session.A2854Mode) return;
        try { Clipboard.SetText(_sessionTimeline.Export()); }
        catch (Exception e)
        {
            _log($"A2854: unable to copy diagnostic timeline ({e.GetType().Name}).");
        }
    }

    /// <summary>Copy only the bounded and sanitized A2854 link summary.</summary>
    [RelayCommand]
    void CopyA2854LastLink()
    {
        if (!_session.A2854Mode) return;
        try { Clipboard.SetText(A2854LastLinkSummary); }
        catch (Exception e)
        {
            _log($"A2854: unable to copy safe connection summary ({e.GetType().Name}).");
        }
    }

    /// <summary>Copy only locally measured X/Y landmarks for support/QA.</summary>
    [RelayCommand]
    void CopyTouchCalibrationPoints()
    {
        try
        {
            string formatted = "A2854 measured clickpad landmarks (signed X/Y):" +
                Environment.NewLine +
                string.Join(Environment.NewLine, CalibrationTargets.Select(name =>
                    _touchReferences.TryGetValue(name, out var p)
                        ? $"{name}: X={p.X}, Y={p.Y}" : $"{name}: not captured"));
            Clipboard.SetText(formatted);
        }
        catch (Exception e)
        {
            TouchCalibrationSummary =
                $"Could not copy touch landmarks ({e.GetType().Name}).";
        }
    }

    [RelayCommand]
    void ToggleTouchCalibrationPreview()
    {
        if (!_session.A2854Mode || _calibratedProjection is null)
        {
            TouchCalibrationPreviewEnabled = false;
            return;
        }
        TouchCalibrationPreviewEnabled = !TouchCalibrationPreviewEnabled;
        ClearA2854VisualContacts();
        foreach (var finger in Fingers) finger.IsVisible = false;
        _trail.Clear();
        TrailPoints = new PointCollection();
        _wasDown = false;
        _calibrationWindow.Clear();
        _calibrationFingerDown = false;
        LiveTouchCoordinates = "No active touch";
    }

    // ---- commands -----------------------------------------------------------------------

    [RelayCommand]
    void StartStop()
    {
        if (_session.CanRestartEndedA2854)
        {
            // The previously stopped peer has already completed native USB
            // cleanup. End the old logical session first, then explicitly
            // reopen under the normal Start safety gate. No background retry.
            StopSession();
            StartSession();
        }
        else if (_session.IsRunning) StopSession();
        else StartSession();
    }

    void RefreshButtons()
    {
        OnPropertyChanged(nameof(StartStopText));
        Preferences?.SessionChanged();
        if (!_session.IsRunning)
        {
            foreach (var f in Fingers) f.IsVisible = false;
            ShowButtons(0);
            ResetGesture();
        }
    }

    // Double-click on a row of the nearby list: a device that is not connected is paired through Windows, as in the BLE scanner.
    [RelayCommand]
    async Task PairDevice()
    {
        if (IsPairing || SelectedNearby is not { Connected: false } row) return;
        // Adapter under WinUSB: the list comes from Windows' adapter, pairing through Windows would bond the remote to
        // the wrong one; the session pairs it itself (Pair button). Double-click does nothing.
        if (!RemoteStartupPolicy.AllowLegacyPairingCommands(_session.A2854Mode) ||
            _session.UsesBluetoothDirect || Preferences?.WinUsbOn == true) return;
        IsPairing = true;
        // Scanning while connecting upsets some adapters: the scan is suspended.
        _scanner.Stop();
        try
        {
            if (await BleScanViewModel.PairAsync(row.Key, row.Name, row.Address, _dialogs, text => { PairText = text; _log(text); })
                && !_session.IsRunning && RemoteStartupPolicy.AllowLegacyPairingCommands(_session.A2854Mode))
                StartSession();   // an in-flight Windows pairing must never launch A2854
        }
        finally
        {
            if (RemoteStartupPolicy.AllowLegacyPairingCommands(_session.A2854Mode))
                _scanner.Start();
            IsPairing = false;
        }
        // The result stays readable a moment, unless another pairing has started since.
        var shown = PairText;
        await Task.Delay(TimeSpan.FromSeconds(6));
        if (!IsPairing && PairText == shown) PairText = "";
    }

    [RelayCommand]
    void Pair()
    {
        if (!RemoteStartupPolicy.AllowLegacyPairingCommands(_session.A2854Mode))
        {
            _log("A2854: legacy pairing is disabled; the protected bond is preserved.");
            return;
        }
        if (_session.UsesDongle)
        {
            // ESP32-S3 dongle: it forgets its bond and scans for a remote in pairing mode by itself.
            _session.SendDongleCommand(EspDongle.CommandPair);
            _log(Loc.T("Log.PairDirect"));
            return;
        }
        if (_session.UsesBluetoothDirect)
        {
            // Direct Bluetooth: forget the bond and restart; the session then looks for a remote in pairing mode.
            _session.Stop();
            _session.ForgetPairing();
            StartSession();
            _log(Loc.T("Log.PairDirect"));
            return;
        }

        _log(Loc.T("Log.PairSettings"));
        try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
        catch (Exception ex) { _log(Loc.T("Log.OpenBtFailed", ex.Message)); }
    }

    // Forget the remote: the dongle stays idle until "Pair"; direct Bluetooth stops; Windows' HID is managed in Settings.
    [RelayCommand]
    void Forget()
    {
        if (!RemoteStartupPolicy.AllowLegacyPairingCommands(_session.A2854Mode))
        {
            _log("A2854: legacy forget is disabled; the protected bond is preserved.");
            return;
        }
        if (_session.UsesDongle) _session.SendDongleCommand(EspDongle.CommandForget);
        else if (_session.UsesBluetoothDirect)
        {
            StopSession();
            _session.ForgetPairing();
        }
        else
        {
            try { Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true }); }
            catch (Exception ex) { _log(Loc.T("Log.OpenBtFailed", ex.Message)); }
            return;
        }
        _log(Loc.T("Log.Forget"));
    }
}
