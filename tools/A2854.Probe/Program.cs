// SPDX-License-Identifier: AGPL-3.0-or-later
// A2854 raw-input probe for an independently controlled WinUSB Bluetooth dongle.
// No Windows HID filter, service bypass, or adapter driver installation.

using System.Diagnostics;
using System.Text.RegularExpressions;
using AppleRemote;
using AppleRemote.Windows;

namespace AppleRemote.A2854Probe;

internal static partial class Program
{
    [GeneratedRegex(@"(?i)(?:[0-9a-f]{2}:){5}[0-9a-f]{2}")]
    private static partial Regex BluetoothAddress();

    static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This probe needs native Windows and a WinUSB Bluetooth controller.");
            return 2;
        }

        int seconds = 30;
        UsbHci.UsbId? targetUsbId = null;
        bool hciOnly = false, scanOnly = false, connectOnly = false, smpNegotiateOnly = false;
        for (int i = 0; i < args.Length;)
        {
            if (args[i] is "--hci-only" or "--scan-only" or "--connect-only" or "--smp-negotiate-only")
            {
                if (args[i] == "--hci-only") hciOnly = true;
                else if (args[i] == "--scan-only") scanOnly = true;
                else if (args[i] == "--connect-only") connectOnly = true;
                else smpNegotiateOnly = true;
                i++;
            }
            else if (i + 1 < args.Length && args[i] == "--seconds" &&
                int.TryParse(args[i + 1], out int duration) && duration is >= 5 and <= 120)
            {
                seconds = duration;
                i += 2;
            }
            else if (i + 1 < args.Length && args[i] == "--usb" &&
                Regex.IsMatch(args[i + 1], @"\A[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\z"))
            {
                string[] id = args[i + 1].Split(':');
                targetUsbId = new UsbHci.UsbId(Convert.ToUInt16(id[0], 16), Convert.ToUInt16(id[1], 16));
                i += 2;
            }
            else
            {
                Console.Error.WriteLine("Usage: A2854.Probe [--seconds 30] [--usb 33FA:0001] [--hci-only | --scan-only | --connect-only | --smp-negotiate-only]");
                return 2;
            }
        }

        if ((hciOnly ? 1 : 0) + (scanOnly ? 1 : 0) + (connectOnly ? 1 : 0) + (smpNegotiateOnly ? 1 : 0) > 1)
        {
            Console.Error.WriteLine("Choose only one of --hci-only, --scan-only, --connect-only, or --smp-negotiate-only.");
            return 2;
        }
        // The experimental link-only mode is deliberately limited to the measured,
        // independently controlled external Barrot adapter. Never select the built-in Intel adapter.
        if ((connectOnly || smpNegotiateOnly) && targetUsbId != new UsbHci.UsbId(0x33FA, 0x0001))
        {
            Console.Error.WriteLine($"{(connectOnly ? "--connect-only" : "--smp-negotiate-only")} requires --usb 33FA:0001 (the dedicated external WinUSB dongle).");
            return 2;
        }

        UsbHci usb;
        try { usb = targetUsbId is { } id ? UsbHci.OpenForUsbId(id) : UsbHci.Open(); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"USB WinUSB interface discovery/open failed: {e.GetType().Name}.");
            Console.Error.WriteLine("An additional USB Bluetooth dongle must be under WinUSB.");
            Console.Error.WriteLine("Do NOT change the laptop's built-in Bluetooth adapter driver.");
            return 2;
        }

        // USB transport-only diagnosis: do not scan, pair, alter bond storage,
        // subscribe to microphone, or send LE commands. One HCI reset and a
        // local-version query suffice to isolate controller availability.
        if (hciOnly)
        {
            using (usb)
            {
                Console.WriteLine($"USB controller: {usb.Name}; HCI EP0 request=0x{usb.CommandRequestType:X2}.");
                try
                {
                    await usb.Host.CommandAsync(HciOpcode.Reset);
                    byte[] version = await usb.Host.CommandAsync(HciOpcode.ReadLocalVersion);
                    if (version.Length < 9)
                        throw new InvalidDataException($"Unexpected Read Local Version length {version.Length} (expected >=9).");
                    Console.WriteLine($"HCI Reset OK; Read Local Version OK: HCI=0x{version[1]:X2}, " +
                        $"revision=0x{(version[2] | version[3] << 8):X4}, " +
                        $"LMP=0x{version[4]:X2}, manufacturer=0x{(version[5] | version[6] << 8):X4}, " +
                        $"subversion=0x{(version[7] | version[8] << 8):X4}.");
                    Console.WriteLine("HCI-only complete: no BLE scan or pairing attempted.");
                    return 0;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"HCI-only FAILED: {e.GetType().Name}: {BluetoothAddress().Replace(e.Message, "[redacted]")}");
                    return 1;
                }
            }
        }

        // Controller/advertising smoke test. This intentionally never connects,
        // requests SMP keys, accesses the GATT HID service or modifies bond storage.
        // Only packet counts and protocol flags are printed: no MAC/serial/AD dump.
        if (scanOnly)
        {
            using (usb)
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(seconds)))
            {
                var central = new LeCentral(usb.Host);
                int packets = 0, apple = 0, hid = 0, both = 0, strong = 0;
                int readmePrefix = 0, capturedPrefix = 0, nameHint = 0, interesting = 0;
                central.Advertisement += (_, rssi, ad) =>
                {
                    A2854AdvertisementSignals signals = A2854Advertisement.Classify(ad);
                    Interlocked.Increment(ref packets);
                    if (signals.AppleCompany) Interlocked.Increment(ref apple);
                    if (signals.HidService) Interlocked.Increment(ref hid);
                    if (signals.AppleCompany && signals.HidService) Interlocked.Increment(ref both);
                    if (signals.ReadmeManufacturerPrefix) Interlocked.Increment(ref readmePrefix);
                    if (signals.CapturedManufacturerPrefix) Interlocked.Increment(ref capturedPrefix);
                    if (signals.RemoteNameHint) Interlocked.Increment(ref nameHint);
                    if (signals.StrongCandidate) Interlocked.Increment(ref strong);
                    if ((signals.StrongCandidate || signals.RemoteNameHint) &&
                        Interlocked.Increment(ref interesting) <= 12)
                        Console.WriteLine($"A2854 candidate: known_prefix={signals.ReadmeManufacturerPrefix || signals.CapturedManufacturerPrefix}, name_hint={signals.RemoteNameHint}, RSSI={rssi} dBm (no address/identity logged)");
                };
                try
                {
                    Console.WriteLine($"BLE scan-only: {usb.Name}; HCI EP0 request=0x{usb.CommandRequestType:X2}.");
                    Console.WriteLine("No BLE connection/pairing/bond writes. Press remote Back + Volume Up ~5s to advertise.");
                    await central.InitializeAsync();
                    Console.WriteLine($"Controller initialized. LE extended advertising enabled: {central.Extended}.");
                    // Never-match predicate keeps scanning regardless of the observed data.
                    // Cancellation stops scanning and sends the disable-scan command.
                    try { await central.ScanPeerAsync(_ => false, allowRandom: true, cts.Token); }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested) { }
                    Console.WriteLine($"Scan summary: packets={packets}, apple_ad_packets={apple}, hid1812_ad_packets={hid}, both_in_one_packet={both}.");
                    Console.WriteLine($"A2854 indicators: strong_candidates={strong}, readme_prefix={readmePrefix}, captured_prefix={capturedPrefix}, name_hints={nameHint}.");
                    Console.WriteLine("Strong candidate = Apple 0x004C + HID 0x1812 + a known A2854 manufacturer header, all in one AD packet.");
                    Console.WriteLine("No BLE connection or pairing was attempted.");
                    return 0;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Scan-only FAILED: {e.GetType().Name}: {BluetoothAddress().Replace(e.Message, "[redacted]")}");
                    return 1;
                }
            }
        }

        // The next hardware gate is intentionally link-only. Do not start the full
        // SiriRemoteBle driver: that would initiate SMP pairing, write a bond,
        // discover GATT HID and subscribe to reports.
        if (connectOnly)
        {
            using (usb)
            using (var stop = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler onCancel = (_, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };
                Console.CancelKeyPress += onCancel;
                bool connectionAttempted = false, linkEnded = false;
                ushort? connectedHandle = null;
                int disconnectRequestStarted = 0;
                var disconnected = new TaskCompletionSource<(ushort Handle, byte Reason, bool AfterRequest)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                string stage = "controller initialization";
                try
                {
                    // Suppress even automatic L2CAP connection-parameter signaling
                    // to ensure this mode sends only link-layer HCI commands.
                    var central = new LeCentral(usb.Host, respondToSignaling: false);
                    // Preserve event timing relative to our explicit HCI Disconnect request.
                    // 0x3E means a link-layer establishment/synchronization failure, NOT
                    // a clean locally initiated disconnect. Never report it as OK.
                    central.Disconnected += (observedHandle, reason) =>
                        disconnected.TrySetResult((observedHandle, reason,
                            Volatile.Read(ref disconnectRequestStarted) != 0));

                    Console.WriteLine($"LE connect-only: {usb.Name}; EP0=0x{usb.CommandRequestType:X2}.");
                    Console.WriteLine("No pairing, SMP, GATT, HID, bond reads/writes, or user data output.");
                    await central.InitializeAsync();
                    Console.WriteLine($"Controller initialized. Extended LE commands: {central.Extended}.");

                    stage = "strong A2854 advertisement scan";
                    using var scanWindow = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    scanWindow.CancelAfter(TimeSpan.FromSeconds(seconds));
                    Console.WriteLine("Starting scan for known A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.");
                    Console.WriteLine("Wake the remote / hold Back + Volume Up ~5s once scan begins.");
                    // ScanPeerAsync stops scanning in its own finally before ConnectAsync begins.
                    LePeer peer = await central.ScanPeerAsync(
                        ad => A2854Advertisement.Classify(ad).StrongCandidate,
                        allowRandom: true, scanWindow.Token);
                    Console.WriteLine($"Strong candidate acquired; address redacted; peer type={(peer.AddressType == 1 ? "random" : "public")}.");

                    stage = "LE Create Connection / LE Connection Complete";
                    using var connectionWindow = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    connectionWindow.CancelAfter(TimeSpan.FromSeconds(12));
                    // Mark this BEFORE issuing the command; even an interrupted or timed-out
                    // initiation is cleaned up with an external-controller HCI Reset.
                    connectionAttempted = true;
                    ushort handle = await central.ConnectAsync(peer.Address, connectionWindow.Token, peer.AddressType);
                    connectedHandle = handle;
                    Console.WriteLine("LE Connection Complete OK (peer address redacted).");
                    var linkTimer = Stopwatch.StartNew();

                    // A short, bounded observation separates spontaneous link failure
                    // from completion of our own disconnect command. This sends NO SMP
                    // or GATT traffic and does not read or persist any BLE identity.
                    stage = "1500ms LE link observation (no pairing/GATT)";
                    Console.WriteLine("Observing link for 1500ms before requesting local disconnect (no SMP/GATT).");
                    Task observation = Task.Delay(TimeSpan.FromMilliseconds(1500), stop.Token);
                    if (await Task.WhenAny(disconnected.Task, observation) == disconnected.Task)
                    {
                        var early = await disconnected.Task;
                        if (early.Handle != handle)
                            throw new InvalidDataException("Unexpected disconnection event handle during observation.");
                        linkEnded = true;
                        Console.Error.WriteLine($"LE link ended BEFORE host Disconnect at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                            $"reason=0x{early.Reason:X2} ({HciDisconnectReason.Describe(early.Reason)}).");
                        return 1;
                    }
                    await observation; // Respect Ctrl+C if the observation timer was cancelled.
                    if (disconnected.Task.IsCompleted)
                    {
                        var early = await disconnected.Task;
                        if (early.Handle != handle)
                            throw new InvalidDataException("Unexpected disconnection event handle immediately before Disconnect.");
                        linkEnded = true;
                        Console.Error.WriteLine($"LE link ended before host Disconnect at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                            $"reason=0x{early.Reason:X2} ({HciDisconnectReason.Describe(early.Reason)}).");
                        return 1;
                    }

                    stage = "HCI Disconnect Command Status / Disconnection Complete";
                    Volatile.Write(ref disconnectRequestStarted, 1);
                    await central.DisconnectAsync(handle);
                    Console.WriteLine("HCI Disconnect Command Status OK.");
                    var ended = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                    if (ended.Handle != handle)
                        throw new InvalidDataException("Disconnection Complete handle did not match LE connection handle.");
                    linkEnded = true;
                    Console.WriteLine($"Disconnection Complete received at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                        $"reason=0x{ended.Reason:X2} ({HciDisconnectReason.Describe(ended.Reason)}), " +
                        $"after_host_request={ended.AfterRequest}.");
                    if (!HciDisconnectReason.IsExpectedHostDisconnect(ended.Reason))
                    {
                        Console.Error.WriteLine("Link is gone, but termination reason is NOT the expected local-host 0x16. " +
                            "Classified as an anomalous link termination, not a verified clean disconnect.");
                        return 1;
                    }
                    Console.WriteLine("LE link established and clean local disconnect verified. No SMP/GATT/bond writes.");
                    return 0;
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine($"Connect-only cancelled/timed out at {stage}. No pairing was attempted.");
                    return 1;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Connect-only FAILED at {stage}: {e.GetType().Name}: {BluetoothAddress().Replace(e.Message, "[redacted]")}");
                    return 1;
                }
                finally
                {
                    // If a matching Disconnection Complete arrived while an earlier
                    // HCI command failed, the link is already gone: avoid an unnecessary
                    // controller reset. The event does NOT imply a clean local termination.
                    if (!linkEnded && connectedHandle is ushort establishedHandle && disconnected.Task.IsCompleted &&
                        disconnected.Task.Result.Handle == establishedHandle)
                    {
                        // A command may throw even though Disconnection Complete arrived
                        // for this exact handle. Do not reset an already-ended external link.
                        linkEnded = true;
                    }
                    if (connectionAttempted && !linkEnded)
                    {
                        // An in-flight LE create-connection can complete late. Resetting
                        // ONLY this dedicated WinUSB controller is the fallback to avoid
                        // leaving an unpaired link alive after errors or Ctrl+C.
                        try
                        {
                            await usb.Host.CommandAsync(HciOpcode.Reset);
                            Console.WriteLine("Cleanup: external Barrot controller HCI Reset OK.");
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine($"Cleanup Reset failed: {e.GetType().Name}. Check dongle state before retry.");
                        }
                    }
                    Console.CancelKeyPress -= onCancel;
                }
            }
        }


        // Diagnostic SMP negotiation-only gate: issues a standard Pairing Request (CID 0x0006)
        // after link establishment, analyzes the remote's Pairing Response / Failed PDU,
        // and immediately disconnects without key exchange, encryption start, or bond persistence.
        if (smpNegotiateOnly)
        {
            using (usb)
            using (var stop = new CancellationTokenSource())
            {
                ConsoleCancelEventHandler onCancel = (_, e) =>
                {
                    e.Cancel = true;
                    stop.Cancel();
                };
                Console.CancelKeyPress += onCancel;
                bool connectionAttempted = false, linkEnded = false;
                ushort? connectedHandle = null;
                int disconnectRequestStarted = 0;
                var disconnected = new TaskCompletionSource<(ushort Handle, byte Reason, bool AfterRequest)>(
                    TaskCreationOptions.RunContinuationsAsynchronously);
                string stage = "controller initialization";
                try
                {
                    // Suppress automatic L2CAP parameter updates; we control the link explicitly
                    var central = new LeCentral(usb.Host, respondToSignaling: false);
                    central.Disconnected += (observedHandle, reason) =>
                        disconnected.TrySetResult((observedHandle, reason,
                            Volatile.Read(ref disconnectRequestStarted) != 0));

                    Console.WriteLine($"LE SMP negotiate-only: {usb.Name}; EP0=0x{usb.CommandRequestType:X2}.");
                    Console.WriteLine("Diagnostics only: sends Pairing Request, analyzes response, disconnects immediately.");
                    Console.WriteLine("NO confirm/random keys, NO encryption start, NO LTK/IRK persistence, NO GATT/HID.");
                    await central.InitializeAsync();
                    Console.WriteLine($"Controller initialized. Extended LE commands: {central.Extended}.");

                    stage = "strong A2854 advertisement scan";
                    using var scanWindow = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    scanWindow.CancelAfter(TimeSpan.FromSeconds(seconds));
                    Console.WriteLine("Starting scan for known A2854 manufacturer prefix + Apple 0x004C + HID 0x1812.");
                    Console.WriteLine("Wake the remote / hold Back + Volume Up ~5s once scan begins.");
                    LePeer peer = await central.ScanPeerAsync(
                        ad => A2854Advertisement.Classify(ad).StrongCandidate,
                        allowRandom: true, scanWindow.Token);
                    Console.WriteLine($"Strong candidate acquired; address redacted; peer type={(peer.AddressType == 1 ? "random" : "public")}.");

                    stage = "LE Create Connection / LE Connection Complete";
                    using var connectionWindow = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
                    connectionWindow.CancelAfter(TimeSpan.FromSeconds(12));
                    connectionAttempted = true;
                    ushort handle = await central.ConnectAsync(peer.Address, connectionWindow.Token, peer.AddressType);
                    connectedHandle = handle;
                    Console.WriteLine("LE Connection Complete OK (peer address redacted).");
                    var linkTimer = Stopwatch.StartNew();

                    stage = "link stabilization check (500ms)";
                    Console.WriteLine("Observing link stability for 500ms before sending SMP Pairing Request.");
                    Task stabilization = Task.Delay(TimeSpan.FromMilliseconds(500), stop.Token);
                    if (await Task.WhenAny(disconnected.Task, stabilization) == disconnected.Task)
                    {
                        var early = await disconnected.Task;
                        if (early.Handle != handle)
                            throw new InvalidDataException("Unexpected disconnection event handle during stabilization.");
                        linkEnded = true;
                        Console.Error.WriteLine($"LE link ended BEFORE SMP negotiation at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                            $"reason=0x{early.Reason:X2} ({HciDisconnectReason.Describe(early.Reason)}).");
                        return 1;
                    }
                    await stabilization;

                    stage = "SMP Pairing Request / Response negotiation";
                    Console.WriteLine("Sending SMP Pairing Request (CID 0x0006): IO=NoInputNoOutput, OOB=0, AuthReq=Bonding(0x01), KeySize=16, RespKeyDist=EncKey(0x01).");
                    var preq = SmpPairingRequest.Default;
                    Task<SmpNegotiationResult> negotiateTask = SmpNegotiator.NegotiateAsync(central, handle, preq, stop.Token);
                    Task completed = await Task.WhenAny(negotiateTask, disconnected.Task);

                    if (completed == disconnected.Task)
                    {
                        var early = await disconnected.Task;
                        if (early.Handle != handle)
                            throw new InvalidDataException("Unexpected disconnection event handle during SMP negotiation.");
                        linkEnded = true;
                        Console.Error.WriteLine($"Link dropped during SMP negotiation at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                            $"reason=0x{early.Reason:X2} ({HciDisconnectReason.Describe(early.Reason)}).");
                        return 1;
                    }

                    SmpNegotiationResult result = await negotiateTask;
                    Console.WriteLine($"SMP Negotiation received at {linkTimer.Elapsed.TotalMilliseconds:F0}ms:");
                    if (result is SmpNegotiationResult.Success s)
                    {
                        var pres = s.Response;
                        Console.WriteLine("=== SMP Pairing Response Analysis ===");
                        Console.WriteLine($"  IO Capability:               {SmpIoCapability.Describe(pres.IoCapability)}");
                        Console.WriteLine($"  OOB Data Flag:               0x{pres.OobDataFlag:X2} ({(pres.OobDataFlag == 1 ? "OOB Present" : "Not Present")})");
                        Console.WriteLine($"  AuthReq:                     0x{pres.AuthReq:X2}");
                        Console.WriteLine($"    - Bonding:                 {pres.BondingRequested}");
                        Console.WriteLine($"    - MITM:                    {pres.MitmRequested}");
                        Console.WriteLine($"    - Secure Connections (SC): {pres.SecureConnectionsRequested}");
                        Console.WriteLine($"    - Keypress:                {pres.KeypressRequested}");
                        Console.WriteLine($"    - CT2:                     {pres.Ct2Requested}");
                        Console.WriteLine($"  Max Encryption Key Size:     {pres.MaxKeySize} bytes");
                        Console.WriteLine($"  Initiator Key Distribution:  {SmpKeyDistribution.Describe(pres.InitiatorKeyDistribution)}");
                        Console.WriteLine($"  Responder Key Distribution:  {SmpKeyDistribution.Describe(pres.ResponderKeyDistribution)}");
                        Console.WriteLine("=== Protocol Assessment ===");
                        if (pres.SecureConnectionsRequested)
                            Console.WriteLine("  Remote supports/prefers: LE Secure Connections (SC bit set in response).");
                        else
                            Console.WriteLine("  Remote accepted/returned: Legacy Pairing (SC bit clear in response).");
                        if (pres.MitmRequested)
                            Console.WriteLine("  Remote requests MITM protection.");
                        else
                            Console.WriteLine("  Remote requests unauthenticated (Just Works) pairing model.");
                        if (pres.HasIdKey)
                            Console.WriteLine("  Remote intends to distribute Identity Resolving Key (IRK) for address resolution.");
                    }
                    else if (result is SmpNegotiationResult.Failed f)
                    {
                        Console.WriteLine("=== SMP Pairing Failed ===");
                        Console.WriteLine($"  Reason Code: 0x{f.ReasonCode:X2} ({f.Description})");
                        if (f.ReasonCode == SmpFailedReason.AuthenticationRequirements)
                        {
                            Console.WriteLine("  CRITICAL: Remote rejected legacy pairing with Authentication Requirements (0x03).");
                            Console.WriteLine("  This strongly indicates the remote requires LE Secure Connections (SC) or MITM.");
                        }
                    }

                    stage = "HCI Disconnect Command Status / Disconnection Complete";
                    Console.WriteLine("Stopping negotiation here (no Confirm/Random/Key exchange). Requesting local disconnect.");
                    Volatile.Write(ref disconnectRequestStarted, 1);
                    await central.DisconnectAsync(handle);
                    Console.WriteLine("HCI Disconnect Command Status OK.");
                    var ended = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5), stop.Token);
                    if (ended.Handle != handle)
                        throw new InvalidDataException("Disconnection Complete handle did not match LE connection handle.");
                    linkEnded = true;
                    Console.WriteLine($"Disconnection Complete received at {linkTimer.Elapsed.TotalMilliseconds:F0}ms: " +
                        $"reason=0x{ended.Reason:X2} ({HciDisconnectReason.Describe(ended.Reason)}), " +
                        $"after_host_request={ended.AfterRequest}.");
                    if (!HciDisconnectReason.IsExpectedHostDisconnect(ended.Reason))
                    {
                        Console.Error.WriteLine("Link terminated with unexpected code: " + HciDisconnectReason.Describe(ended.Reason));
                        return 1;
                    }
                    Console.WriteLine("LE SMP negotiation diagnostic completed successfully and link cleanly disconnected.");
                    return result is SmpNegotiationResult.Success ? 0 : 1;
                }
                catch (OperationCanceledException)
                {
                    Console.Error.WriteLine($"SMP negotiate-only cancelled/timed out at {stage}.");
                    return 1;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"SMP negotiate-only FAILED at {stage}: {e.GetType().Name}: {BluetoothAddress().Replace(e.Message, "[redacted]")}");
                    return 1;
                }
                finally
                {
                    if (!linkEnded && connectedHandle is ushort establishedHandle && disconnected.Task.IsCompleted &&
                        disconnected.Task.Result.Handle == establishedHandle)
                    {
                        linkEnded = true;
                    }
                    if (connectionAttempted && !linkEnded)
                    {
                        try
                        {
                            await usb.Host.CommandAsync(HciOpcode.Reset);
                            Console.WriteLine("Cleanup: external Barrot controller HCI Reset OK.");
                        }
                        catch (Exception e)
                        {
                            Console.Error.WriteLine($"Cleanup Reset failed: {e.GetType().Name}. Check dongle state before retry.");
                        }
                    }
                    Console.CancelKeyPress -= onCancel;
                }
            }
        }
        // Full BLE workflow only if expressly selected without --hci-only.
        using (usb)
        using (var cts = new CancellationTokenSource())
        {
            Loc.SetLanguage("en");
            var stopwatch = Stopwatch.StartNew();
            var decoder = new A2854Decoder();
            var gate = new object();
            int buttons = 0, touches = 0, malformed = 0;
            string bondsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "AppleRemote", "a2854-probe-bonds.json");

            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            void OnStatus(string message)
            {
                // Human-readable statuses may embed BLE addresses; redact them.
                string safe = BluetoothAddress().Replace(message, "[redacted BLE address]");
                Console.WriteLine("status: " + safe);
            }

            void OnReport(double time, byte[]? report)
            {
                if (report is null) return;
                lock (gate)
                {
                    if (report.Length == 0)
                    {
                        foreach (var ev in decoder.Decode(report))
                            if (ev is ButtonsEvent b)
                                Console.WriteLine($"link lost: released {string.Join(",", b.Released)}");
                        Console.WriteLine("link lost: input state reset");
                        return;
                    }

                    // The GATT subscription is restricted to 0xFB/0xFC;
                    // microphone report 0xFA is never requested or printed.
                    if (report[0] is not (0xFB or 0xFC)) return;
                    var decoded = decoder.Decode(report);

                    if (report[0] == A2854Decoder.ButtonsReportId)
                    {
                        buttons++;
                        foreach (var ev in decoded)
                        {
                            if (ev is ButtonsEvent b)
                                Console.WriteLine($"button +{string.Join("+", b.Pressed)} -{string.Join("+", b.Released)} mask=0x{b.Mask:X4}");
                        }
                    }
                    else
                    {
                        touches++;
                        var parsed = A2854Decoder.ParseTouch(report.AsSpan(1));
                        if (parsed is null) malformed++;
                        if (touches <= 80)
                        {
                            string points = parsed is null
                                ? "malformed"
                                : string.Join(" ",
                                    new[] { parsed.First, parsed.Second }
                                        .Select((f, i) => f is { } finger
                                            ? $"slot{i + 1}(x={finger.X},y={finger.Y},hover={finger.Hover},pressure={finger.Pressure},flags=0x{finger.Flags:X2})"
                                            : ""));
                            Console.WriteLine($"touch: {points} raw={Convert.ToHexString(report)}");
                        }
                        else if (touches == 81)
                        {
                            Console.WriteLine("touch output capped at 80 frames (still counting)");
                        }
                    }
                }
            }

            Console.WriteLine("A2854 diagnostic: WinUSB HCI, buttons 0xFB + touch 0xFC only.");
            Console.WriteLine($"USB controller: {usb.Name}; HCI EP0 request=0x{usb.CommandRequestType:X2}.");
            Console.WriteLine("Use --usb 33FA:0001 to restrict this probe to the purchased Barrot adapter.");
            Console.WriteLine("Pairing mode if needed: hold Back + Volume Up for ~5 seconds.");
            Console.WriteLine("Existing Windows Bluetooth pairings do NOT transfer to a new controller.");
            Console.WriteLine("No HID driver, adapter driver, or microphone changes are made by this program.");

            var driver = new SiriRemoteBle(usb.Host, new BondStore(bondsPath),
                OnReport, OnStatus, adapterName: "WinUSB",
                inputReportIds: new HashSet<byte> { 0xFB, 0xFC }, allowRandomAddress: true);

            Task run = driver.RunAsync(cts.Token);
            try
            {
                await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(seconds), cts.Token));
            }
            finally
            {
                cts.Cancel();
                try { await run.WaitAsync(TimeSpan.FromSeconds(6)); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { Console.Error.WriteLine("Shutdown timed out."); }
                catch (Exception e)
                {
                    Console.Error.WriteLine($"Direct BLE error: {e.GetType().Name}: {BluetoothAddress().Replace(e.Message, "[redacted]")}");
                }
            }

            Console.WriteLine($"summary: buttons={buttons}, touch_frames={touches}, malformed_touch={malformed}, elapsed={stopwatch.Elapsed.TotalSeconds:F1}s");
            Console.WriteLine("This is a hardware experiment, not a claim of A2854 Windows compatibility.");
            return run.IsFaulted ? 1 : 0;
        }
    }
}
