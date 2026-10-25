// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

/// <summary>
/// Optional standard Bluetooth Battery Service (0x180F), Battery Level (0x2A19).
/// No A2854-specific hard-coded handles, writes, descriptor subscriptions or
/// proprietary battery/charging assumptions.
/// </summary>
public static class A2854Battery
{
    public const ushort ServiceUuid = 0x180F;
    public const ushort LevelUuid = 0x2A19;
    const byte ReadProperty = 0x02;

    /// <summary>Only a unique, readable, in-range level value can be used.</summary>
    public static ushort? FindReadableLevel(
        GattService service, IReadOnlyList<GattCharacteristic> characteristics)
    {
        if (!GattClient.Is(service.Uuid, ServiceUuid)) return null;
        var matches = characteristics.Where(c =>
            GattClient.Is(c.Uuid, LevelUuid) &&
            (c.Properties & ReadProperty) != 0 &&
            c.Declaration >= service.Start && c.Declaration <= service.End &&
            c.Value > c.Declaration && c.Value <= service.End).ToArray();
        return matches.Length == 1 ? matches[0].Value : null;
    }

    /// <summary>A valid Battery Level is exactly one byte in the range 0..100.</summary>
    public static int? DecodeLevel(ReadOnlySpan<byte> value) =>
        value.Length == 1 && value[0] <= 100 ? value[0] : null;

    /// <summary>
    /// A single optional read once HID input has been enabled. A device
    /// without the standard service simply returns null; no ATT writes occur.
    /// Transport errors/timeouts are handled by the caller as nonfatal.
    /// </summary>
    public static async Task<int?> ReadOnceAsync(GattClient gatt,
        CancellationToken ct = default)
    {
        var services = await gatt.DiscoverPrimaryServicesAsync(ct);
        var battery = services.Where(s => GattClient.Is(s.Uuid, ServiceUuid)).ToArray();
        if (battery.Length != 1) return null;
        var chars = await gatt.DiscoverCharacteristicsAsync(battery[0], ct);
        ushort? handle = FindReadableLevel(battery[0], chars);
        if (handle is null) return null;
        return DecodeLevel(await gatt.ReadAsync(handle.Value, ct));
    }
}
