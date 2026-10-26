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
    readonly A2854TouchDiagnostics _touchDiagnostics = new();
    readonly KeyboardInjector _injector = new();
    readonly A2854PointerMapper _pointer;
    readonly KeyBinder _binder;
    bool _a2854Mode, _a2854PointerEnabled, _acceptA2854Input;
    bool _a2854PointerUseCalibration, _a2854ClickAsKeyboard;
    int _a2854HidReady; // set only after encrypted GATT HID subscriptions succeed
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
        _pointer = new A2854PointerMapper(new A2854DiagnosticPointerSink(_touchDiagnostics));
        _pointer.NoMoveObserved = _touchDiagnostics.Suppressed;
        // Both producers must share a VK owner registry; otherwise Ctrl+C
        // can lift a held physical C when the shortcut completes.
        _binder = new KeyBinder(map ?? KeyMap.Default(), _injector.PhysicalKeys);
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

    /// <summary>Aggregate-only A2854 touch telemetry; passive until Start.</summary>
    public A2854TouchDiagnostics TouchDiagnostics => _touchDiagnostics;

    void ReportKeyboardReleaseResult()
    {
        // These are separate producers: physical KeyBinder and shortcut/macro
        // calls through KeyboardInjector.Key(). Both must finish cleanup.
        bool physicalReleased = _binder.ReleaseAll();
        bool shortcutsReleased = _injector.ReleaseShortcutKeys();
        if (!physicalReleased || !shortcutsReleased)
        {
            try { Status?.Invoke("A2854: Windows rejected a keyboard key-up; release remains pending for cleanup retry."); }
            catch { /* diagnostics must not prevent cleanup */ }
        }
    }

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
                // This option must have no side effects on first-generation
                // keyboard state. Only A2854 pointer-mode changes require
                // releasing its keyboard binding alongside the mouse click.
                if (_a2854Mode)
                    ReportKeyboardReleaseResult();
                if (_a2854Mode || _pointer.HasHeldClick)
                {
                    if (!_pointer.Reset())
                        Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
                }
                _a2854PointerEnabled = value;
                if (value && _a2854PointerUseCalibration)
                    ReloadPointerCalibration();
            }
        }
    }

    /// <summary>
    /// Optional A2854 center-button routing while pointer movement stays on.
    /// False (default): physical click is Windows left mouse. True: route it
    /// through the existing user-editable keyboard binding instead. Keyboard
    /// injection remains separately opt-in.
    /// </summary>
    public bool A2854ClickAsKeyboard
    {
        get { lock (_gate) return _a2854ClickAsKeyboard; }
        set
        {
            lock (_gate)
            {
                if (_a2854ClickAsKeyboard == value) return;
                if (_a2854Mode)
                {
                    // Changing the owner of a held click must not strand a
                    // mouse button or a mapped keyboard key in its down state.
                    ReportKeyboardReleaseResult();
                    if (!_pointer.Reset())
                        Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
                }
                _a2854ClickAsKeyboard = value;
            }
        }
    }

    /// <summary>
    /// Apply the user's saved measured clickpad axes when enabled in settings.
    /// The pointer itself remains
    /// OFF unless A2854PointerEnabled is separately enabled. A failed profile
    /// load leaves the original relative mapper active, never guesses bounds.
    /// </summary>
    public bool A2854PointerUseCalibration
    {
        get { lock (_gate) return _a2854PointerUseCalibration; }
        set
        {
            lock (_gate)
            {
                _a2854PointerUseCalibration = value;
                ReloadPointerCalibration();
            }
        }
    }

    void ReloadPointerCalibration()
    {
        _pointer.Calibration = null;
        if (!_a2854PointerUseCalibration) return;
        try
        {
            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AppleRemote", "A2854-touch-calibration.json");
            var points = A2854TouchCalibrationStore.Load(path);
            if (A2854TouchCalibration.TryCreate(points, out var profile))
            {
                _pointer.Calibration = profile;
                Status?.Invoke("A2854: measured physical clickpad pointer axes are active.");
            }
            else
                Status?.Invoke("A2854: no valid five-point pointer calibration; using original cursor axes.");
        }
        catch (Exception e)
        {
            Status?.Invoke($"A2854: pointer calibration not loaded ({e.GetType().Name}); original axes retained.");
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
                if (_keyboardEnabled && !value) ReportKeyboardReleaseResult();
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

    /// <summary>
    /// Peer disconnect leaves an explicit running token until Stop is called.
    /// Surface a safe manual retry only after the old transport has finished
    /// AND the native USB cleanup has succeeded. Never reopen a live reader.
    /// </summary>
    public bool CanRestartEndedA2854
    {
        get
        {
            lock (_lifecycleGate)
                return RemoteStartupPolicy.CanOfferManualReconnect(
                    _a2854Mode, _cts is not null,
                    _driverTask?.IsCompleted == true,
                    _a2854CleanupTask?.IsCompletedSuccessfully == true,
                    Volatile.Read(ref _a2854HidReady) != 0);
        }
    }

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
            if (_a2854CleanupTask is { } previousCleanup)
            {
                if (!previousCleanup.IsCompleted)
                {
                    Status?.Invoke("A2854: previous USB session is still closing; restart after cleanup completes.");
                    return;
                }
                if (!previousCleanup.IsCompletedSuccessfully)
                {
                    Status?.Invoke("A2854: previous USB cleanup failed; restart is blocked.");
                    return;
                }
            }
            _a2854CleanupTask = null;
            if (_a2854Mode) _touchDiagnostics.Reset();
            Interlocked.Exchange(ref _a2854HidReady, 0);
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
                a2854Hci.WatchFailure(status =>
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
                });
                // A BLE task can be canceled during an ordinary Stop even
                // when WinUSB shutdown succeeds. Track actual cleanup only.
                var usbCleanup = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _a2854CleanupTask = usbCleanup.Task;
                var a2854 = new A2854RemoteBle(a2854Hci.Host,
                    report => FeedA2854(report, transportFault),
                    status => Status?.Invoke(status), onTerminalCleanup: () =>
                    {
                        lock (_gate) _acceptA2854Input = false;
                        ReleaseA2854HeldInputs(); // independent fallback if GUI event callback threw
                    }, onReady: () => Interlocked.Exchange(ref _a2854HidReady, 1),
                    onBattery: level => Battery?.Invoke(level),
                    touchDiagnostics: _touchDiagnostics);
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
                        try
                        {
                            try
                            {
                                a2854Hci.Dispose();
                                // This signal tracks USB cleanup, not BLE task
                                // cancellation or other transport outcomes.
                                await a2854Hci.CleanupCompletion.ConfigureAwait(false);
                            }
                            finally { transportFault.Dispose(); }
                            usbCleanup.TrySetResult();
                        }
                        catch (Exception e)
                        {
                            usbCleanup.TrySetException(e);
                            throw;
                        }
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
            // Dedicated A2854 Barrot must never be selected by the
            // first-generation SMP/BondStore fallback, even if its mode is off.
            try { hci = UsbHci.OpenForLegacyRemote(AdapterId); }
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
                    finally
                    {
                        hci.Dispose();
                        await hci.CleanupCompletion.ConfigureAwait(false);
                    }
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
            if (cts is null)
            {
                // A previous Stop may have finished USB/session teardown but
                // Windows could have rejected its keyboard key-up. Retrying
                // Stop must still attempt that pending release without
                // restarting BLE, sending extra input or repeating teardown.
                lock (_gate)
                {
                    if (_binder.HasPendingReleases || _injector.HasPendingShortcutReleases)
                        ReportKeyboardReleaseResult();
                }
                return;
            }

            bool wasA2854 = _a2854Mode;
            Task? activeDriver = _driverTask;
            try
            {
                try { cts.Cancel(); }
                catch (Exception e)
                {
                    try { Status?.Invoke($"Remote: cancellation callback failed ({e.GetType().Name})."); }
                    catch { }
                }

                if (wasA2854)
                {
                    // Revoke incoming input before notifying subscribers.
                    lock (_gate) _acceptA2854Input = false;
                    try { FeedA2854([]); }
                    catch (Exception e)
                    {
                        // Never skip _cts/null cleanup due to GUI event code.
                        try { Status?.Invoke($"A2854: Stop release callback failed ({e.GetType().Name})."); }
                        catch { }
                    }
                }

                try
                {
                    Task[] pending = new[] { _driverTask, _tickTask }
                        .Where(t => t is not null).Cast<Task>().ToArray();
                    if (pending.Length > 0) Task.WaitAll(pending, 2000);
                }
                catch (Exception e)
                {
                    try { Status?.Invoke($"Remote: Stop wait interrupted ({e.GetType().Name})."); }
                    catch { }
                }
            }
            finally
            {
                // Session lifecycle cleanup must happen on every path, even
                // when CancellationToken callbacks or terminal GUI listeners
                // throw. The previous USB owner cannot be reopened until its
                // asynchronous cleanup has actually finished.
                // _a2854CleanupTask is the dedicated native-cleanup signal,
                // NOT activeDriver (which may be Canceled after normal Stop).
                // Keep it until the next Start confirms successful cleanup.
                cts.Dispose();
                _cts = null;
                _driverTask = _tickTask = null;
                AdapterInUse = null;
                _dongle = null;
                lock (_gate)
                {
                    try { ReportKeyboardReleaseResult(); }
                    catch (Exception e)
                    {
                        try { Status?.Invoke($"Remote: keyboard cleanup failed ({e.GetType().Name})."); }
                        catch { }
                    }
                    try
                    {
                        if (!_pointer.Reset())
                            Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
                    }
                    catch (Exception e)
                    {
                        try { Status?.Invoke($"A2854: pointer cleanup failed ({e.GetType().Name})."); }
                        catch { }
                    }
                }
                UsesBluetoothDirect = false;
                try { Status?.Invoke(Loc.T("Status.Stopped")); }
                catch { }
            }
        }
    }

    void FeedA2854(byte[]? report, A2854SessionFaultGate? faultGate = null)
    {
        bool receiverFailed = false;
        lock (_gate)
        {
            // A terminal release sentinel must always run to completion, even
            // when a GUI event subscriber or KeyBinder throws. Old notifications
            // are forbidden once input has been revoked.
            bool terminal = report is { Length: 0 };
            if (!_acceptA2854Input && !terminal && report is { Length: > 0 }) return;
            if (terminal) _acceptA2854Input = false;

            try
            {
                foreach (RemoteEvent ev in _a2854Decoder.Decode(report))
                {
                    if (ev is TouchEvent) _touchDiagnostics.SessionDecoded();
                    if (_a2854PointerEnabled &&
                        (ev is not ButtonsEvent ||
                         A2854ButtonRouting.PointerOwnsClick(_a2854PointerEnabled, _a2854ClickAsKeyboard)))
                    {
                        long pointerBegan = ev is TouchEvent ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
                        try { _pointer.OnEvent(ev); }
                        finally
                        {
                            if (ev is TouchEvent) _touchDiagnostics.PointerProcessed(pointerBegan);
                        }
                        if (ev is ButtonsEvent transition &&
                            (transition.Pressed.Contains("click") || transition.Released.Contains("click")) &&
                            !_pointer.LastButtonTransitionSucceeded)
                            Status?.Invoke("A2854: Windows rejected a mouse button transition; mouse-up is still pending.");
                    }

                    if (_keyboardEnabled)
                    {
                        _binder.OnEvent(ev is ButtonsEvent buttons
                            ? A2854ButtonRouting.ForKeyboard(
                                buttons, _a2854PointerEnabled, _a2854ClickAsKeyboard)
                            : ev);
                        if (_binder.HasPendingReleases)
                            Status?.Invoke("A2854: Windows rejected a keyboard key-up; release remains pending for cleanup retry.");
                    }
                    Event?.Invoke(ev);
                }
            }
            catch (Exception e)
            {
                // An app/UI subscriber can throw on a non-terminal button
                // press. Such exceptions must never escape to USB reader
                // threads or leave an injected click held until user Stop.
                receiverFailed = true;
                _acceptA2854Input = false;
                try { Status?.Invoke($"A2854: input callback failed ({e.GetType().Name}); session is stopping."); }
                catch { /* continue forced input release */ }
            }
            finally
            {
                if (terminal || receiverFailed) ReleaseA2854HeldInputs();
            }
        }
        // Do not acquire the USB-fault gate while holding _gate: its own
        // revocation takes _gate and could otherwise deadlock with this path.
        if (receiverFailed && faultGate is not null)
        {
            try { faultGate.Fail(); }
            catch (Exception e)
            {
                try { Status?.Invoke($"A2854: input fault cancellation failed ({e.GetType().Name})."); }
                catch { }
            }
        }
    }

    // A terminal HID event and an independent InputGate fallback both reach
    // this method. Each backend is released even if the other fails.
    void ReleaseA2854HeldInputs()
    {
        lock (_gate)
        {
            try { ReportKeyboardReleaseResult(); }
            catch (Exception e)
            {
                try { Status?.Invoke($"A2854: keyboard release failed ({e.GetType().Name})."); }
                catch { }
            }
            try
            {
                if (!_pointer.Reset())
                    Status?.Invoke("A2854: Windows rejected mouse-up; held click retained for cleanup retry.");
            }
            catch (Exception e)
            {
                try { Status?.Invoke($"A2854: pointer release failed ({e.GetType().Name})."); }
                catch { }
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
