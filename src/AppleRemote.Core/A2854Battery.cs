// SPDX-License-Identifier: AGPL-3.0-or-later

namespace AppleRemote;

public enum A2854BatteryQueryOutcome
{
    BatteryServiceMissing,
    BatteryServiceAmbiguous,
    BatteryLevelMissing,
    BatteryLevelAmbiguous,
    BatteryLevelUnreadable,
    InvalidPayload,
    Success
}

public readonly record struct A2854BatteryInspection(
    A2854BatteryQueryOutcome Outcome, int? LevelPercent);

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
        var inspection = await InspectOnceAsync(gatt, ct);
        return inspection.LevelPercent;
    }

    /// <summary>Inspects the standard battery metadata and reads its level once.</summary>
    public static async Task<A2854BatteryInspection> InspectOnceAsync(GattClient gatt,
        CancellationToken ct = default)
    {
        var services = await gatt.DiscoverPrimaryServicesAsync(ct);
        var battery = services.Where(s => GattClient.Is(s.Uuid, ServiceUuid)).ToArray();
        if (battery.Length == 0)
            return new(A2854BatteryQueryOutcome.BatteryServiceMissing, null);
        if (battery.Length > 1)
            return new(A2854BatteryQueryOutcome.BatteryServiceAmbiguous, null);

        List<GattCharacteristic> chars = await gatt.DiscoverCharacteristicsAsync(battery[0], ct);

        var levels = chars.Where(c => GattClient.Is(c.Uuid, LevelUuid) &&
            c.Declaration >= battery[0].Start && c.Declaration <= battery[0].End).ToArray();
        if (levels.Length == 0)
            return new(A2854BatteryQueryOutcome.BatteryLevelMissing, null);
        if (levels.Length > 1)
            return new(A2854BatteryQueryOutcome.BatteryLevelAmbiguous, null);

        var level = levels[0];
        if ((level.Properties & ReadProperty) == 0 || level.Value <= level.Declaration ||
            level.Value > battery[0].End)
            return new(A2854BatteryQueryOutcome.BatteryLevelUnreadable, null);

        byte[] value = await gatt.ReadAsync(level.Value, ct);

        int? percent = DecodeLevel(value);
        return percent is int valid
            ? new(A2854BatteryQueryOutcome.Success, valid)
            : new(A2854BatteryQueryOutcome.InvalidPayload, null);
    }
}
