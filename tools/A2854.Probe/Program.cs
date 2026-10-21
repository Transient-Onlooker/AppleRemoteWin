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
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length)
            {
                Console.Error.WriteLine("Usage: A2854.Probe [--seconds 30] [--usb 33FA:0001]");
                return 2;
            }
            if (args[i] == "--seconds" && int.TryParse(args[i + 1], out int duration) &&
                duration is >= 5 and <= 120)
                seconds = duration;
            else if (args[i] == "--usb" && Regex.IsMatch(args[i + 1], @"\A[0-9A-Fa-f]{4}:[0-9A-Fa-f]{4}\z"))
            {
                string[] id = args[i + 1].Split(':');
                targetUsbId = new UsbHci.UsbId(Convert.ToUInt16(id[0], 16), Convert.ToUInt16(id[1], 16));
            }
            else
            {
                Console.Error.WriteLine("Usage: A2854.Probe [--seconds 30] [--usb 33FA:0001]");
                return 2;
            }
        }

        UsbHci usb;
        try { usb = targetUsbId is { } id ? UsbHci.OpenForUsbId(id) : UsbHci.Open(); }
        catch (Exception e)
        {
            Console.Error.WriteLine($"WinUSB controller unavailable: {e.GetType().Name}.");
            Console.Error.WriteLine("An additional USB Bluetooth dongle must be under WinUSB.");
            Console.Error.WriteLine("Do NOT change the laptop's built-in Bluetooth adapter driver.");
            return 2;
        }

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
