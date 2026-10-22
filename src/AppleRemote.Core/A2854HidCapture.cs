// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Narrow A2854 HID input experiment: no pairing, no microphone, no Windows input injection.
// A caller must already have an encrypted LE connection and a dedicated GATT client.

namespace AppleRemote;

/// <summary>
/// Verified HID report layout and dynamically discovered CCCDs. Attribute handles are
/// per-firmware-instance values and must NEVER be inferred from neighboring handles.
/// </summary>
public sealed record A2854HidInputMap(
    ushort ButtonValue, ushort ButtonCccd,
    ushort TouchValue, ushort TouchCccd,
    ushort UnlockFeatureValue);

/// <summary>
/// Discovers ONLY reports 0xFB and 0xFC and the F0 feature; enables only their
/// CCCDs and sends a single observed F0 &lt;- AF unlock. No microphone or HID OS injection.
/// </summary>
public static class A2854HidCapture
{
    private const ushort HidService = 0x1812, Report = 0x2A4D;
    private const ushort ReportReference = 0x2908, Cccd = 0x2902;
    private const byte Input = 1, Feature = 3, NotifyProperty = 0x10, WriteProperty = 0x08;

    /// <summary>
    /// Discard unexpected report characteristics. A2854 value handles observed on
    /// the real remote are optional additional validation for the first physical gate.
    /// </summary>
    public static async Task<A2854HidInputMap> DiscoverAsync(GattClient gatt,
        CancellationToken cancellationToken = default, bool requireObservedValueHandles = false)
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

        var map = new A2854HidInputMap(
            buttons.Value.Value, buttons.Value.Cccd,
            touch.Value.Value, touch.Value.Cccd,
            feature.Value);
        if (requireObservedValueHandles &&
            (map.ButtonValue != 0x0039 || map.TouchValue != 0x003D || map.UnlockFeatureValue != 0x004D))
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
        await gatt.WriteAsync(map.ButtonCccd, cancellationToken, 0x01, 0x00);
        await gatt.WriteAsync(map.TouchCccd, cancellationToken, 0x01, 0x00);
        await gatt.WriteAsync(map.UnlockFeatureValue, cancellationToken, 0xAF);
    }

    /// <summary>Best-effort cleanup when the encrypted link is still alive.</summary>
    public static async Task DisableAsync(GattClient gatt, A2854HidInputMap map,
        CancellationToken cancellationToken = default)
    {
        await gatt.WriteAsync(map.ButtonCccd, cancellationToken, 0x00, 0x00);
        await gatt.WriteAsync(map.TouchCccd, cancellationToken, 0x00, 0x00);
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
