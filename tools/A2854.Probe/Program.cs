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
        bool hciOnly = false, scanOnly = false;
        for (int i = 0; i < args.Length;)
        {
            if (args[i] == "--hci-only" || args[i] == "--scan-only")
            {
                if (args[i] == "--hci-only") hciOnly = true;
                else scanOnly = true;
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
                Console.Error.WriteLine("Usage: A2854.Probe [--seconds 30] [--usb 33FA:0001] [--hci-only | --scan-only]");
                return 2;
            }
        }

        if (hciOnly && scanOnly)
        {
            Console.Error.WriteLine("Choose either --hci-only or --scan-only, not both.");
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
                int packets = 0, apple = 0, hid = 0, both = 0, interesting = 0;
                central.Advertisement += (_, rssi, ad) =>
                {
                    bool hasApple = false, hasHid = false;
                    foreach (var (type, value) in LeCentral.AdStructures(ad))
                    {
                        if (type == 0xFF && value.Length >= 2 && value[0] == 0x4C && value[1] == 0x00)
                            hasApple = true;
                        if (type is 0x02 or 0x03 &&
                            Enumerable.Range(0, value.Length / 2).Any(i =>
                                value[2 * i] == 0x12 && value[2 * i + 1] == 0x18))
                            hasHid = true;
                    }
                    Interlocked.Increment(ref packets);
                    if (hasApple) Interlocked.Increment(ref apple);
                    if (hasHid) Interlocked.Increment(ref hid);
                    if (hasApple && hasHid) Interlocked.Increment(ref both);
                    if ((hasApple || hasHid) && Interlocked.Increment(ref interesting) <= 12)
                        Console.WriteLine($"AD: apple={hasApple}, hid1812={hasHid}, RSSI={rssi} dBm (address redacted)");
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
