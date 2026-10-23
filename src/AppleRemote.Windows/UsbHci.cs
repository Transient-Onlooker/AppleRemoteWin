// SPDX-License-Identifier: AGPL-3.0-or-later
// Copyright (C) 2026 AnthoDingo

using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using static AppleRemote.Windows.Native.NativeMethods;

namespace AppleRemote.Windows;

/// <summary>
/// Bluetooth controller driven directly over USB (HCI "H2" transport) via WinUSB, without the Windows Bluetooth stack:
/// commands via control transfers, events via the interrupt endpoint, ACL data via the bulk endpoints.
/// The adapter must have the WinUSB driver (Zadig); Windows can then no longer use it.
/// </summary>
public sealed class UsbHci : IDisposable, IAsyncDisposable
{
    readonly SafeFileHandle _file;
    readonly IntPtr _usb;
    readonly byte _eventPipe, _aclInPipe, _aclOutPipe, _commandRequestType;
    // Reader startup and shutdown share a gate; release is permitted only
    // after every started reader has actually exited.
    readonly object _readerLifecycle = new();
    readonly UsbReaderShutdown _shutdown;
    Thread? _eventReader, _aclReader;
    Task? _cleanupTask;
    volatile bool _disposed;

    public HciHost Host { get; }

    /// <summary>Adapter name for the logs: USB product string and VID:PID, e.g. "TP-Link UB500 Adapter (2357:0604)".</summary>
    public string Name { get; }

    /// <summary>PnP instance ID (<c>USB\VID_…&amp;PID_…\serial</c>), as in the settings.</summary>
    public string InstanceId { get; }

    /// <summary>USB HCI EP0 request type (0x20 default; 0x21 for documented Barrot models).</summary>
    public byte CommandRequestType => _commandRequestType;

    /// <summary>USB read error (adapter removed, driver changed…). Raised from a reader thread.</summary>
    public event Action<string>? Failed;

    // The two reader threads start in the constructor, before RemoteSession
    // can attach to Failed. Preserve their earliest error for the A2854 path.
    readonly A2854UsbFailureRelay _earlyFailure = new();

    /// <summary>
    /// Subscribe to the first reader failure, replaying one that happened
    /// during USB adapter construction. Unlike Failed, cannot lose the initial
    /// error. Used only by the dedicated A2854 connection path.
    /// </summary>
    public void WatchFailure(Action<string> handler) => _earlyFailure.Subscribe(handler);

    UsbHci(SafeFileHandle file, IntPtr usb, string instanceId)
    {
        InstanceId = instanceId;
        _file = file;
        _usb = usb;
        (_eventPipe, _aclInPipe, _aclOutPipe) = FindPipes(usb);
        Name = ReadName(usb, out UsbId? deviceId);
        _commandRequestType = deviceId is { } id
            ? UsbHciTransportQuirks.CommandRequestType(id.Vid, id.Pid)
            : (byte)0x20;
        Host = new HciHost(SendCommand, SendAcl);
        // Construction is passive. Ownership must transfer before readers
        // can start so no partial startup takes the unsafe outer rollback path.
        _shutdown = new UsbReaderShutdown(
            ReadersAlive, AbortActiveReaders, WaitForReaders, ReleaseNativeHandles);
    }

    void StartReaders()
    {
        lock (_readerLifecycle)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UsbHci));
            _eventReader = CreateReader(_eventPipe, 260, Host.ReceiveEventBytes, "events");
            _aclReader = CreateReader(_aclInPipe, 1024, Host.ReceiveAclBytes, "ACL");
            _eventReader.Start();
            _aclReader.Start();
        }
    }

    bool ReadersAlive() =>
        _eventReader?.IsAlive == true || _aclReader?.IsAlive == true;

    void AbortActiveReaders()
    {
        // Attempt both independently. Failed native aborts are retried by the
        // lifetime worker while the handles remain owned.
        if (_eventReader?.IsAlive == true)
        {
            try { WinUsb_AbortPipe(_usb, _eventPipe); }
            catch (Exception e) { System.Diagnostics.Trace.TraceWarning($"USB event abort failed: {e.GetType().Name}"); }
        }
        if (_aclReader?.IsAlive == true)
        {
            try { WinUsb_AbortPipe(_usb, _aclInPipe); }
            catch (Exception e) { System.Diagnostics.Trace.TraceWarning($"USB ACL abort failed: {e.GetType().Name}"); }
        }
    }

    void WaitForReaders()
    {
        // Invoked only on the cleanup worker, never from a reader callback.
        if (_eventReader?.IsAlive == true) _eventReader.Join(250);
        if (_aclReader?.IsAlive == true) _aclReader.Join(250);
    }

    void ReleaseNativeHandles()
    {
        // Called once only after BOTH reader threads have exited.
        try
        {
            if (_usb != IntPtr.Zero && !WinUsb_Free(_usb))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "WinUsb_Free failed");
        }
        finally { _file.Dispose(); }
    }

    /// <summary>USB identifier (VID/PID) of an adapter.</summary>
    public readonly record struct UsbId(ushort Vid, ushort Pid)
    {
        public override string ToString() => $"{Vid:X4}:{Pid:X4}";
    }

    /// <summary>Paths of the present USB devices.</summary>
    // Must match WinUsbDriver.BuildInf DeviceInterfaceGUIDs. The USB hub's
    // GUID_DEVINTERFACE_USB_DEVICE is not a substitute for the WinUSB function
    // device interface (see Microsoft's WinUSB installation documentation).
    public static readonly Guid WinUsbAdapterInterfaceGuid =
        new("6B3E8C52-9D41-4F6A-8A1E-5C0D7B2F4E19");

    static IEnumerable<string> EnumerateUsbPaths()
    {
        // Look under the actual WinUSB function interface first. Retain the
        // legacy enumeration as a compatibility fallback for older installs.
        foreach (Guid interfaceGuid in new[] { WinUsbAdapterInterfaceGuid, GUID_DEVINTERFACE_USB_DEVICE })
        {
            Guid guid = interfaceGuid;
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "SetupDiGetClassDevs failed");
            try
            {
                for (uint index = 0; ; index++)
                {
                    var data = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, index, ref data)) break;
                    string? path = HidEnumerator.GetInterfacePath(set, ref data);
                    if (path is not null) yield return path;
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(set);
            }
        }
    }

    /// <summary>PnP instance ID of an interface path: <c>\\?\usb#vid_x&amp;pid_y#serial#{guid}</c> -> <c>usb\vid_x&amp;pid_y\serial</c>.</summary>
    static string PathInstanceId(string path) => path[4..path.LastIndexOf('#')].Replace('#', '\\');

    /// <summary>
    /// Opens a USB device under WinUSB whose interface 0 is a Bluetooth controller (class 0xE0, subclass 1, protocol 1),
    /// whatever its VID/PID: the one with this PnP instance ID, or the first found if null.
    /// </summary>
    /// <param name="present">The device with this instance ID is plugged in (whatever its driver).</param>
    static UsbHci? TryOpenAdapter(string? instanceId, out bool present,
        UsbId? targetId = null, UsbId? excludeId = null)
    {
        present = false;
        foreach (string path in EnumerateUsbPaths())
        {
            string candidate = PathInstanceId(path);
            if (instanceId is not null && !candidate.Equals(instanceId, StringComparison.OrdinalIgnoreCase)) continue;
            if (targetId is { } id &&
                !candidate.Contains($"VID_{id.Vid:X4}&PID_{id.Pid:X4}", StringComparison.OrdinalIgnoreCase))
                continue;
            if (excludeId is { } excluded &&
                candidate.Contains($"VID_{excluded.Vid:X4}&PID_{excluded.Pid:X4}", StringComparison.OrdinalIgnoreCase))
                continue;
            present = true;
            var handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero,
                OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            IntPtr usb = IntPtr.Zero;
            bool ownershipTransferred = false;
            try
            {
                if (handle.IsInvalid) continue;
                if (!WinUsb_Initialize(handle, out usb)) continue;
                if (!WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor) ||
                    descriptor.bInterfaceClass != 0xE0 ||
                    descriptor.bInterfaceSubClass != 1 || descriptor.bInterfaceProtocol != 1)
                    continue;

                // Constructor is passive. Transfer ownership BEFORE starting
                // readers so any partial startup is cleaned up by the owner.
                var controller = new UsbHci(handle, usb, candidate);
                ownershipTransferred = true;
                try
                {
                    controller.StartReaders();
                    return controller;
                }
                catch
                {
                    try { controller.Dispose(); }
                    catch (Exception e)
                    {
                        System.Diagnostics.Trace.TraceError($"USB startup cleanup failed: {e.GetType().Name}");
                    }
                    throw;
                }
            }
            finally
            {
                if (!ownershipTransferred)
                {
                    // Preserve the primary constructor exception if native
                    // cleanup itself throws (normally WinUSB reports failures
                    // with a false return rather than a managed exception).
                    try { if (usb != IntPtr.Zero) WinUsb_Free(usb); }
                    catch (Exception e)
                    {
                        System.Diagnostics.Trace.TraceError($"WinUSB rollback failed: {e.GetType().Name}");
                    }
                    try { handle.Dispose(); }
                    catch (Exception e)
                    {
                        System.Diagnostics.Trace.TraceError($"USB file rollback failed: {e.GetType().Name}");
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Opens the USB Bluetooth adapter with this PnP instance ID (the one chosen in the settings) if it is plugged in;
    /// otherwise (absent, or no choice) the first one under WinUSB. A chosen adapter that is present but not under WinUSB
    /// is not replaced by another one: the session then uses Windows' stack, i.e. that adapter.
    /// </summary>
    public static UsbHci Open(string? instanceId = null)
    {
        bool present = false;
        if (instanceId is not null && TryOpenAdapter(instanceId, out present) is { } chosen) return chosen;
        if (present)
            throw new InvalidOperationException($"The chosen Bluetooth adapter ({instanceId}) is not under WinUSB (or already in use).");
        return TryOpenAdapter(null, out _) ?? throw new InvalidOperationException(
            "No USB Bluetooth adapter under WinUSB (WinUSB driver installed, PC restarted since, adapter not already in use?).");
    }

    /// <summary>
    /// Existing first-generation WPF path. Never selects the dedicated A2854
    /// Barrot 33FA:0001, including when a saved legacy adapter disappears and
    /// generic WinUSB auto-fallback would otherwise pick Barrot. Other adapters
    /// retain the same selection and fallback behavior as UsbHci.Open().
    /// </summary>
    public static UsbHci OpenForLegacyRemote(string? instanceId = null)
    {
        var dedicated = new UsbId(0x33FA, 0x0001);
        if (UsbHciTransportQuirks.IsDedicatedA2854Path(instanceId))
            throw new InvalidOperationException(
                "Dedicated Barrot 33FA:0001 cannot use the first-generation pairing driver. Select A2854 mode.");

        bool present = false;
        if (instanceId is not null &&
            TryOpenAdapter(instanceId, out present, excludeId: dedicated) is { } chosen)
            return chosen;
        if (present)
            throw new InvalidOperationException(
                $"The chosen Bluetooth adapter ({instanceId}) is not available under WinUSB.");
        return TryOpenAdapter(null, out _, excludeId: dedicated)
            ?? throw new InvalidOperationException(
                "No legacy Bluetooth adapter available under WinUSB. Barrot 33FA:0001 is A2854-only.");
    }

    /// <summary>
    /// Read-only device-interface enumeration; never opens a USB handle or sends
    /// HCI traffic. Used to prevent the legacy GUI auto-start path from treating
    /// a dedicated A2854 Barrot as the first available generic WinUSB adapter.
    /// </summary>
    public static bool IsWinUsbDevicePresent(UsbId id)
    {
        string token = $"VID_{id.Vid:X4}&PID_{id.Pid:X4}";
        return EnumerateUsbPaths().Any(path =>
            path.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Opens only a dedicated WinUSB adapter matching the requested USB VID/PID.
    /// Useful for avoiding inadvertent selection of unrelated adapters during experiments.
    /// </summary>
    public static UsbHci OpenForUsbId(UsbId id) =>
        TryOpenAdapter(null, out _, id) ?? throw new InvalidOperationException(
            $"No USB Bluetooth adapter {id} available under WinUSB.");

    static (byte Event, byte AclIn, byte AclOut) FindPipes(IntPtr usb)
    {
        byte evt = 0, aclIn = 0, aclOut = 0;
        if (!WinUsb_QueryInterfaceSettings(usb, 0, out var descriptor))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Lecture de l'interface USB impossible");
        for (byte i = 0; i < descriptor.bNumEndpoints; i++)
        {
            if (!WinUsb_QueryPipe(usb, 0, i, out var pipe)) continue;
            bool input = (pipe.PipeId & 0x80) != 0;
            if (pipe.PipeType == UsbdPipeTypeInterrupt && input) evt = pipe.PipeId;
            else if (pipe.PipeType == UsbdPipeTypeBulk && input) aclIn = pipe.PipeId;
            else if (pipe.PipeType == UsbdPipeTypeBulk) aclOut = pipe.PipeId;
        }
        if (evt == 0 || aclIn == 0 || aclOut == 0)
            throw new InvalidOperationException("This USB device doesn't have the endpoints of a Bluetooth controller.");
        return (evt, aclIn, aclOut);
    }

    static string ReadName(IntPtr usb, out UsbId? identity)
    {
        identity = null;
        var device = new byte[18];
        if (!WinUsb_GetDescriptor(usb, 0x01, 0, 0, device, (uint)device.Length, out uint read) || read < 18)
            return "USB adapter";
        identity = new UsbId((ushort)(device[8] | device[9] << 8), (ushort)(device[10] | device[11] << 8));
        string id = identity.Value.ToString();
        var text = new byte[255];
        // iProduct (byte 15), US English; string descriptor = length, type, UTF-16LE.
        if (device[15] == 0 || !WinUsb_GetDescriptor(usb, 0x03, device[15], 0x0409, text, (uint)text.Length, out read) || read < 2)
            return id;
        return $"{System.Text.Encoding.Unicode.GetString(text, 2, (int)Math.Min(read, text[0]) - 2).Trim()} ({id})";
    }

    void SendCommand(byte[] packet)
    {
        // Barrot 33FA:0001/0010 uses class/interface (0x21) on EP0; the original
        // Realtek/gen-1 path retains class/device (0x20).
        var setup = new WINUSB_SETUP_PACKET { RequestType = _commandRequestType, Length = (ushort)packet.Length };
        if (!WinUsb_ControlTransfer(_usb, setup, packet, (uint)packet.Length, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Cannot send the HCI command");
    }

    void SendAcl(byte[] packet)
    {
        if (!WinUsb_WritePipe(_usb, _aclOutPipe, packet, (uint)packet.Length, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Envoi ACL impossible");
    }

    Thread CreateReader(byte pipe, int size, SpanAction receive, string name)
    {
        var thread = new Thread(() =>
        {
            var buffer = new byte[size];
            while (!_disposed)
            {
                if (WinUsb_ReadPipe(_usb, pipe, buffer, (uint)size, out uint read, IntPtr.Zero))
                {
                    if (read > 0)
                    {
                        try { receive(buffer.AsSpan(0, (int)read)); }
                        catch (Exception e)
                        {
                            // HID subscribers, including third-party WPF callbacks,
                            // must never terminate this background thread with an
                            // unhandled exception. Surface the transport failure
                            // through the same A2854 cancellation/release path.
                            string reason = $"USB HCI ({name}) packet callback failed: {e.GetType().Name}";
                            try { _earlyFailure.Fail(reason); }
                            catch { /* owner cleanup must not escape a USB reader */ }
                            try { Failed?.Invoke(reason); }
                            catch { /* protect reader shutdown */ }
                            return;
                        }
                    }
                }
                else if (!_disposed)
                {
                    string reason = $"Lecture USB ({name}) interrompue : {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
                    // Record before invoking legacy handlers; a failure can
                    // predate RemoteSession's subscription or throw in a UI.
                    try { _earlyFailure.Fail(reason); }
                    catch { /* an input/status handler must not crash the reader thread */ }
                    try { Failed?.Invoke(reason); }
                    catch { /* preserve existing event without corrupting reader shutdown */ }
                    return;
                }
            }
        }) { IsBackground = true, Name = $"UsbHci {name}" };
        return thread;
    }

    delegate void SpanAction(ReadOnlySpan<byte> data);

    /// <summary>
    /// Completes only after both readers exit and native handles are freed;
    /// a Join timeout by itself is never considered successful cleanup.
    /// </summary>
    public Task CleanupCompletion
    {
        get { lock (_readerLifecycle) return _cleanupTask ?? Task.CompletedTask; }
    }

    public void Dispose()
    {
        Task cleanup;
        lock (_readerLifecycle)
        {
            _disposed = true;
            cleanup = _cleanupTask ??= _shutdown.Begin();
        }

        // Do not hang the UI forever when the OS refuses to abort a read:
        // the worker keeps the handles owned until safe release is possible.
        if (Thread.CurrentThread == _eventReader || Thread.CurrentThread == _aclReader)
            return;
        try { cleanup.Wait(TimeSpan.FromSeconds(1)); }
        catch (AggregateException e)
        {
            System.Diagnostics.Trace.TraceError($"USB cleanup failed: {e.GetBaseException().GetType().Name}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        Dispose();
        await CleanupCompletion.ConfigureAwait(false);
    }
}
