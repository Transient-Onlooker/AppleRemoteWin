// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Narrow A2854 HID input experiment: no pairing, no microphone, no Windows input injection.
// A caller must already have an encrypted LE connection and a dedicated GATT client.

namespace AppleRemote;

/// <summary>
/// Diagnostic capture quality, independent of which side closed the LE link.
/// </summary>
public enum A2854HidCaptureOutcome
{
    BothReportsCaptured,
    PartialReportsCaptured,
    NoReportsCaptured,
    UnexpectedLinkTermination,
}

public static class A2854HidCaptureAssessment
{
    /// <summary>
    /// Null reason: observation timer completed while linked. 0x13: peer/user
    /// terminated the link. All other reasons are unexpected. Receiving a valid
    /// 0x13 is NOT equivalent to a verified host-initiated 0x16 disconnect.
    /// </summary>
    public static A2854HidCaptureOutcome Evaluate(
        int buttonFrames, int touchFrames, byte? remoteDisconnectReason)
    {
        if (buttonFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(buttonFrames), "Frame count must be nonnegative.");
        if (touchFrames < 0)
            throw new ArgumentOutOfRangeException(nameof(touchFrames), "Frame count must be nonnegative.");
        if (remoteDisconnectReason is not null and not 0x13)
            return A2854HidCaptureOutcome.UnexpectedLinkTermination;
        if (buttonFrames > 0 && touchFrames > 0)
            return A2854HidCaptureOutcome.BothReportsCaptured;
        if (buttonFrames > 0 || touchFrames > 0)
            return A2854HidCaptureOutcome.PartialReportsCaptured;
        return A2854HidCaptureOutcome.NoReportsCaptured;
    }
}

/// <summary>
/// Verified HID report layout and dynamically discovered CCCDs. Attribute handles are
/// per-firmware-instance values and must NEVER be inferred from neighboring handles.
/// </summary>
public sealed class A2854HidInputMap
{
    public ushort ButtonValue { get; }
    public ushort ButtonCccd { get; }
    public ushort TouchValue { get; }
    public ushort TouchCccd { get; }
    public ushort UnlockFeatureValue { get; }
    internal bool WasDiscovered { get; }
    internal GattClient? DiscoveredBy { get; }

    public A2854HidInputMap(ushort buttonValue, ushort buttonCccd,
        ushort touchValue, ushort touchCccd, ushort unlockFeatureValue)
        : this(buttonValue, buttonCccd, touchValue, touchCccd, unlockFeatureValue, false, null) { }

    private A2854HidInputMap(ushort buttonValue, ushort buttonCccd,
        ushort touchValue, ushort touchCccd, ushort unlockFeatureValue, bool wasDiscovered,
        GattClient? discoveredBy)
    {
        ButtonValue = buttonValue;
        ButtonCccd = buttonCccd;
        TouchValue = touchValue;
        TouchCccd = touchCccd;
        UnlockFeatureValue = unlockFeatureValue;
        WasDiscovered = wasDiscovered;
        DiscoveredBy = discoveredBy;
    }

    internal static A2854HidInputMap CreateDiscovered(GattClient gatt, ushort buttonValue,
        ushort buttonCccd, ushort touchValue, ushort touchCccd, ushort unlockFeatureValue) =>
        new(buttonValue, buttonCccd, touchValue, touchCccd, unlockFeatureValue, true, gatt);
}

/// <summary>
/// Discovers ONLY reports 0xFB and 0xFC and the F0 feature; enables only their
/// CCCDs and sends a single observed F0 &lt;- AF unlock. No microphone or HID OS injection.
/// </summary>
public static class A2854HidCapture
{
    private const ushort HidService = 0x1812, Report = 0x2A4D;
    private const ushort ReportReference = 0x2908, Cccd = 0x2902;
    private const ushort ButtonValue = 0x0039, TouchValue = 0x003D, UnlockFeatureValue = 0x004D;
    private const byte Input = 1, Feature = 3, NotifyProperty = 0x10, WriteProperty = 0x08;

    /// <summary>
    /// Discard unexpected report characteristics and fail closed unless all three
    /// value handles match the observed A2854 mapping.
    /// </summary>
    public static async Task<A2854HidInputMap> DiscoverAsync(GattClient gatt,
        CancellationToken cancellationToken = default)
    {
        List<GattService> services = await gatt.DiscoverPrimaryServicesAsync(cancellationToken);
        List<GattService> hidServices = services.Where(s => GattClient.Is(s.Uuid, HidService)).ToList();
        if (hidServices.Count != 1) throw new InvalidDataException("Expected exactly one HID service 0x1812.");
        GattService hid = hidServices[0];
        List<GattCharacteristic> chars = await gatt.DiscoverCharacteristicsAsync(hid, cancellationToken);

        (ushort Value, ushort Cccd)? buttons = null, touch = null;
        ushort? feature = null;
        for (int i = 0; i < chars.Count; i++)
        {
            GattCharacteristic c = chars[i];
            ushort end = i + 1 < chars.Count ? (ushort)(chars[i + 1].Declaration - 1) : hid.End;
            if (!GattClient.Is(c.Uuid, Report) || c.Value >= end) continue;

            var descriptors = await gatt.DiscoverDescriptorsAsync(
                (ushort)(c.Value + 1), end, cancellationToken);
            var refs = descriptors.Where(d => GattClient.Is(d.Uuid, ReportReference)).ToList();
            if (refs.Count == 0) continue;
            if (refs.Count > 1) throw new InvalidDataException("Duplicate HID Report Reference descriptors.");

            byte[] idAndType = await gatt.ReadAsync(refs[0].Handle, cancellationToken);
            if (idAndType.Length != 2) throw new InvalidDataException("Invalid HID Report Reference length.");
            byte id = idAndType[0], type = idAndType[1];

            // The microphone 0xFA and every unknown ID are intentionally ignored:
            // their CCCDs are neither resolved for writes nor subscribed.
            if (id == 0xFB && type == Input)
            {
                if ((c.Properties & NotifyProperty) == 0)
                    throw new InvalidDataException("A2854 0xFB lacks Notify property.");
                if (buttons.HasValue) throw new InvalidDataException("Duplicate A2854 0xFB input.");
                buttons = (c.Value, ExactlyOneCccd(descriptors));
            }
            else if (id == 0xFC && type == Input)
            {
                if ((c.Properties & NotifyProperty) == 0)
                    throw new InvalidDataException("A2854 0xFC lacks Notify property.");
                if (touch.HasValue) throw new InvalidDataException("Duplicate A2854 0xFC input.");
                touch = (c.Value, ExactlyOneCccd(descriptors));
            }
            else if (id == 0xF0 && type == Feature)
            {
                if ((c.Properties & WriteProperty) == 0)
                    throw new InvalidDataException("A2854 0xF0 lacks ATT Write Request property.");
                if (feature.HasValue) throw new InvalidDataException("Duplicate A2854 0xF0 feature.");
                feature = c.Value;
            }
        }

        if (buttons is null || touch is null || feature is null)
            throw new InvalidDataException("A2854 required 0xFB/0xFC inputs or 0xF0 feature are missing.");
        if (buttons.Value.Value == touch.Value.Value ||
            buttons.Value.Cccd == touch.Value.Cccd ||
            buttons.Value.Cccd == feature.Value || touch.Value.Cccd == feature.Value)
            throw new InvalidDataException("Overlapping A2854 HID handles.");

        var map = A2854HidInputMap.CreateDiscovered(gatt,
            buttons.Value.Value, buttons.Value.Cccd,
            touch.Value.Value, touch.Value.Cccd,
            feature.Value);
        if (!map.WasDiscovered || map.ButtonValue != ButtonValue || map.TouchValue != TouchValue ||
            map.UnlockFeatureValue != UnlockFeatureValue)
            throw new InvalidDataException(
                "HID value handles differ from real 2026-10-22 A2854 mapping; stop before any writes.");
        return map;
    }

    private static ushort ExactlyOneCccd(List<(ushort Handle, byte[] Uuid)> descriptors)
    {
        var cccds = descriptors.Where(d => GattClient.Is(d.Uuid, Cccd)).ToList();
        if (cccds.Count != 1) throw new InvalidDataException("Expected exactly one CCCD for HID input report.");
        return cccds[0].Handle;
    }

    /// <summary>
    /// The ONLY attribute writes permitted by the narrow A2854 HID-input gate:
    /// 0x0001 to the two discovered input CCCDs, then 0xAF to the F0 feature.
    /// This is a one-way feature unlock and may persist until disconnect.
    /// </summary>
    public static async Task EnableAsync(GattClient gatt, A2854HidInputMap map,
        CancellationToken cancellationToken = default)
    {
        ValidateWriteMap(gatt, map);
        try
        {
            await gatt.WriteAsync(map.ButtonCccd, cancellationToken, 0x01, 0x00);
            await gatt.WriteAsync(map.TouchCccd, cancellationToken, 0x01, 0x00);
            await gatt.WriteAsync(map.UnlockFeatureValue, cancellationToken, 0xAF);
        }
        catch
        {
            // A write may have reached the peer even when its response was lost.
            // Best effort disable both subscriptions, even if the caller canceled.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await gatt.WriteAsync(map.ButtonCccd, cleanup.Token, 0x00, 0x00); }
            catch { }
            try { await gatt.WriteAsync(map.TouchCccd, cleanup.Token, 0x00, 0x00); }
            catch { }
            throw;
        }
    }

    /// <summary>Best-effort cleanup when the encrypted link is still alive.</summary>
    public static async Task DisableAsync(GattClient gatt, A2854HidInputMap map,
        CancellationToken cancellationToken = default)
    {
        ValidateWriteMap(gatt, map);
        Exception? firstFailure = null;
        try { await gatt.WriteAsync(map.ButtonCccd, cancellationToken, 0x00, 0x00); }
        catch (Exception ex) { firstFailure = ex; }
        try { await gatt.WriteAsync(map.TouchCccd, cancellationToken, 0x00, 0x00); }
        catch (Exception ex) { firstFailure ??= ex; }
        if (firstFailure is not null) throw firstFailure;
    }

    private static void ValidateWriteMap(GattClient gatt, A2854HidInputMap map)
    {
        if (!map.WasDiscovered || !ReferenceEquals(map.DiscoveredBy, gatt) ||
            map.ButtonValue != ButtonValue || map.TouchValue != TouchValue ||
            map.UnlockFeatureValue != UnlockFeatureValue ||
            map.ButtonCccd == map.TouchCccd ||
            map.ButtonCccd == map.UnlockFeatureValue || map.TouchCccd == map.UnlockFeatureValue ||
            map.ButtonValue == map.ButtonCccd || map.ButtonValue == map.TouchCccd ||
            map.TouchValue == map.ButtonCccd || map.TouchValue == map.TouchCccd)
            throw new InvalidDataException("Invalid A2854 HID write map.");
    }

    /// <summary>Accept ONLY the two agreed input report value handles.</summary>
    public static bool TryDecode(A2854Decoder decoder, A2854HidInputMap map,
        ushort valueHandle, byte[] value, out List<RemoteEvent> events, out byte reportId)
    {
        reportId = 0;
        events = [];
        if (valueHandle == map.ButtonValue) reportId = A2854Decoder.ButtonsReportId;
        else if (valueHandle == map.TouchValue) reportId = A2854Decoder.TouchReportId;
        else return false;
        events = decoder.Decode([reportId, .. value]);
        return true;
    }
}
