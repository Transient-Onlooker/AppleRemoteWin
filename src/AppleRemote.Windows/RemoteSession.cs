// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

namespace AppleRemote.Windows;

/// <summary>
/// Orchestrates the HID driver, decoder, gestures and virtual keyboard. Events are raised from pool threads:
/// the UI must marshal them (Dispatcher). The whole chain is serialised by a lock.
/// </summary>
public sealed class RemoteSession : IDisposable
{
    readonly object _gate = new();
    readonly object _lifecycleGate = new(); // serialize Start/Stop across UI/device-change callbacks
    readonly RemotePipeline _pipeline = new();
    readonly A2854Decoder _a2854Decoder = new();
    readonly KeyboardInjector _injector = new();
    readonly A2854PointerMapper _pointer = new(new WindowsPointerSink());
    readonly KeyBinder _binder;
    bool _a2854Mode, _a2854PointerEnabled, _acceptA2854Input;
    Task? _a2854CleanupTask;
    bool _keyboardEnabled;

    CancellationTokenSource? _cts;
    Task? _driverTask, _tickTask;

    public event Action<RemoteEvent>? Event;
    public event Action<GestureEvent>? Gesture;
    public event Action<string>? Status;

    /// <summary>Battery level in %, raised from a pool thread.</summary>
    public event Action<int>? Battery;

    public RemoteSession(KeyMap? map = null)
    {
        _binder = new KeyBinder(map ?? KeyMap.Default(), _injector);
        _pipeline.Event += ev =>
        {
            if (_keyboardEnabled) _binder.OnEvent(ev);
            Event?.Invoke(ev);
        };
        _pipeline.Gesture += g =>
        {
            if (_keyboardEnabled) _binder.OnGesture(g);
            Gesture?.Invoke(g);
        };
    }

    public KeyboardInjector Injector => _injector;

    /// <summary>
    /// Explicitly opt into the third-generation USB-C A2854 driver. Must be
    /// selected before Start; the default 1st-gen/HID/ESP path is unchanged.
    /// Only dedicated Barrot USB VID:PID 33FA:0001 may be opened in this mode.
    /// </summary>
    public bool A2854Mode
    {
        get => _a2854Mode;
        set
        {
            if (IsRunning) throw new InvalidOperationException("Stop the current session before switching remote model.");
            _a2854Mode = value;
        }
    }

    /// <summary>Windows relative pointer and click injection; OFF by default even in A2854 mode.</summary>
    public bool A2854PointerEnabled
    {
        get => _a2854PointerEnabled;
        set
        {
            lock (_gate)
            {
                if (_a2854PointerEnabled == value) return;
                // Switching while a physical button is held must not strand
                // an Enter key from the old mode or a mouse-down in the new one.
                _binder.ReleaseAll();
                if (!_pointer.Reset())
                    Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
                _a2854PointerEnabled = value;
            }
        }
    }

    public double A2854PointerSensitivity
    {
        get { lock (_gate) return _pointer.Sensitivity; }
        set { lock (_gate) _pointer.Sensitivity = value; }
    }

    public bool KeyboardEnabled
    {
        get => _keyboardEnabled;
        set
        {
            lock (_gate)
            {
                if (_keyboardEnabled && !value) _binder.ReleaseAll();
                _keyboardEnabled = value;
            }
        }
    }

    public KeyMap KeyMap
    {
        get => _binder.Map;
        set { lock (_gate) _binder.Map = value; }
    }

    public bool IsRunning => _cts is not null;

    /// <summary>Pairing keys for direct Bluetooth.</summary>
    public string BondsPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AppleRemote", "bonds.json");

    /// <summary>PnP instance ID of the adapter to drive directly when several are under WinUSB (null: the first one).</summary>
    public string? AdapterId { get; set; }

    /// <summary>True if the running session drives the adapter directly (WinUSB) rather than Windows' HID.</summary>
    public bool UsesBluetoothDirect { get; private set; }

    /// <summary>PnP instance ID of the WinUSB adapter the running session drives; null on Windows' HID or when stopped.</summary>
    public string? AdapterInUse { get; private set; }

    EspDongle? _dongle;

    /// <summary>True if the running session reads an ESP32-S3 dongle (firmware: AppleRemoteWin-Dongle).</summary>
    public bool UsesDongle => _dongle is not null;

    /// <summary>Sends <see cref="EspDongle.CommandPair"/>, <see cref="EspDongle.CommandForget"/>… to the dongle.</summary>
    public bool SendDongleCommand(byte command) => _dongle?.Send(command) ?? false;

    /// <summary>Forgets the direct-Bluetooth bond: on next start, looks for a remote in pairing mode.</summary>
    public void ForgetPairing() => new BondStore(BondsPath).Clear();

    /// <summary>
    /// Starts reading the real remote: direct Bluetooth if a USB Bluetooth adapter is under WinUSB
    /// (the Windows Bluetooth stack doesn't keep the remote, see CLAUDE.md), otherwise Windows' HID.
    /// </summary>
    public void Start()
    {
        lock (_lifecycleGate)
        {
            if (_cts is not null) return;
            if (_a2854CleanupTask is { IsCompleted: false })
            {
                Status?.Invoke("A2854: previous USB session is still closing; restart after cleanup completes.");
                return;
            }
            _a2854CleanupTask = null;
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;

            // A2854 is a deliberate, dedicated external-controller selection.
            // Never let the old ESP32 / first-generation / internal HID autodetection
            // override it or silently initiate new SMP pairing.
            if (_a2854Mode)
            {
                UsbHci? a2854Hci;
                try { a2854Hci = UsbHci.OpenForUsbId(new UsbHci.UsbId(0x33FA, 0x0001)); }
                catch (Exception e)
                {
                    Status?.Invoke($"A2854: dedicated Barrot WinUSB interface unavailable ({e.GetType().Name}).");
                    lock (_gate) _acceptA2854Input = false;
                    _cts.Dispose();
                    _cts = null;
                    return;
                }

                lock (_gate) _acceptA2854Input = true;
                UsesBluetoothDirect = true;
                AdapterInUse = a2854Hci.InstanceId;
                // Reader failures do not reliably generate an HCI Disconnection
                // Complete event. Cancel the BLE task AND release held inputs
                // immediately instead of waiting forever for the remote.
                var transportFault = new A2854SessionFaultGate(ct, () =>
                {
                    lock (_gate)
                    {
                        _acceptA2854Input = false;
                        FeedA2854([]); // mouse-up/key-up, serialized with input callbacks
                    }
                });
                a2854Hci.Failed += status =>
                {
                    try { Status?.Invoke(status); }
                    catch { /* status subscribers must not break USB fault cleanup */ }
                    try { transportFault.Fail(); }
                    catch (Exception e)
                    {
                        // Fault gate itself cancels the BLE task even if release
                        // callbacks fail. Never let an error kill the USB reader.
                        try { Status?.Invoke($"A2854: input release failed ({e.GetType().Name})."); }
                        catch { }
                    }
                };
                var a2854 = new A2854RemoteBle(a2854Hci.Host, FeedA2854, status => Status?.Invoke(status));
                _driverTask = Task.Run(async () =>
                {
                    try { await a2854.RunAsync(transportFault.Token); }
                    catch (Exception e) when (!ct.IsCancellationRequested)
                    {
                        Status?.Invoke($"A2854: transport error ({e.GetType().Name}).");
                        FeedA2854([]);
                    }
                    finally
                    {
                        a2854Hci.Dispose();
                        transportFault.Dispose();
                    }
                });
                return; // A2854 data is decoded independently; no legacy gesture tick.
            }

            // ESP32-S3 dongle first: it holds the remote by itself, no adapter driver or filter needed.
            if (EspDongle.Find() is not null)
            {
                var dongle = _dongle = new EspDongle(Feed, s => Status?.Invoke(s), l => Battery?.Invoke(l));
                UsesBluetoothDirect = false;
                _driverTask = Task.Run(() => dongle.RunAsync(ct));
                _tickTask = Task.Run(() => TickLoopAsync(ct));
                return;
            }

            UsbHci? hci = null;
            try { hci = UsbHci.Open(AdapterId); }
            catch (Exception e) { Status?.Invoke(Loc.T("Status.BtUnavailable", e.Message)); }

            UsesBluetoothDirect = hci is not null;
            AdapterInUse = hci?.InstanceId;
            if (hci is not null)
            {
                hci.Failed += s => Status?.Invoke(s);
                var driver = new SiriRemoteBle(hci.Host, new BondStore(BondsPath), Feed, s => Status?.Invoke(s), l => Battery?.Invoke(l), hci.Name);
                _driverTask = Task.Run(async () =>
                {
                    try { await driver.RunAsync(ct); }
                    catch (Exception e) when (!ct.IsCancellationRequested) { Status?.Invoke(Loc.T("Status.BtError", e.Message)); }
                    finally { hci.Dispose(); }
                });
            }
            else
            {
                var driver = new SiriRemoteDriver(Feed, s => Status?.Invoke(s), l => Battery?.Invoke(l));
                _driverTask = Task.Run(() => driver.RunAsync(ct));
            }
            _tickTask = Task.Run(() => TickLoopAsync(ct));
        }
    }

    public void Stop()
    {
        lock (_lifecycleGate)
        {
            var cts = _cts;
            if (cts is null) return;
            cts.Cancel();
            if (_a2854Mode)
            {
                lock (_gate) _acceptA2854Input = false;
                FeedA2854([]); // force held 0xFB buttons and pointer click up immediately
            }
            Task? activeDriver = _driverTask;
            try { Task.WaitAll(new[] { _driverTask, _tickTask }.Where(t => t is not null).Cast<Task>().ToArray(), 2000); }
            catch (AggregateException) { }
            if (_a2854Mode && activeDriver is { IsCompleted: false })
                _a2854CleanupTask = activeDriver; // prevent opening same WinUSB dongle during asynchronous teardown
            cts.Dispose();
            _cts = null;
            _driverTask = _tickTask = null;
            AdapterInUse = null;
            _dongle = null;
            lock (_gate)
            {
                _binder.ReleaseAll();
                if (!_pointer.Reset())
                        Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
            }
            UsesBluetoothDirect = false;
            Status?.Invoke(Loc.T("Status.Stopped"));
        }
    }

    void FeedA2854(byte[]? report)
    {
        lock (_gate)
        {
            // A late USB notification after Stop must never inject mouse/key
            // input into Windows. An empty release sentinel is always accepted.
            if (!_acceptA2854Input && report is { Length: > 0 }) return;
            foreach (RemoteEvent ev in _a2854Decoder.Decode(report))
            {
                if (_a2854PointerEnabled)
                {
                    _pointer.OnEvent(ev);
                    if (ev is ButtonsEvent transition &&
                        (transition.Pressed.Contains("click") || transition.Released.Contains("click")) &&
                        !_pointer.LastButtonTransitionSucceeded)
                        Status?.Invoke("A2854: Windows rejected a mouse button transition; mouse-up is still pending.");
                }

                if (_keyboardEnabled)
                {
                    // A mouse click must not also emit the default gen-1 Enter key
                    // or any configured keyboard/macro action for the click button.
                    if (_a2854PointerEnabled && ev is ButtonsEvent buttons)
                    {
                        _binder.OnEvent(new ButtonsEvent(buttons.Mask,
                            buttons.Pressed.Where(name => name != "click" &&
                                name is not ("tv" or "siri" or "power")).ToArray(),
                            buttons.Released.Where(name => name != "click" &&
                                name is not ("tv" or "siri" or "power")).ToArray(), buttons.Raw));
                    }
                    else if (ev is ButtonsEvent limited)
                        _binder.OnEvent(new ButtonsEvent(limited.Mask,
                            limited.Pressed.Where(name => name is not ("tv" or "siri" or "power")).ToArray(),
                            limited.Released.Where(name => name is not ("tv" or "siri" or "power")).ToArray(),
                            limited.Raw));
                    else _binder.OnEvent(ev);
                }
                Event?.Invoke(ev); // keeps existing GUI event display and key-binding actions
            }
            if (report is { Length: 0 })
            {
                _binder.ReleaseAll();
                if (!_pointer.Reset())
                    Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
            }
        }
    }

    void Feed(double hostTime, byte[]? report)
    {
        lock (_gate) _pipeline.Feed(hostTime, report);
    }

    async Task TickLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
                lock (_gate) _pipeline.Feed(SiriRemoteDriver.HostNow, null);
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose() => Stop();
}
