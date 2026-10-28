// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854BatteryTests
{
    static readonly GattService BatteryService = new(0x0010, 0x001A, [0x0F, 0x18]);
    static readonly GattCharacteristic BatteryLevel = new(0x0011, 0x02, 0x0012, [0x19, 0x2A]);

    [Fact]
    public void Finds_only_unique_readable_standard_battery_level()
    {
        GattCharacteristic[] chars =
        [
            BatteryLevel,
            new(0x0014, 0x10, 0x0015, [0x1A, 0x2A])
        ];
        Assert.Equal((ushort)0x0012, A2854Battery.FindReadableLevel(BatteryService, chars));
    }

    [Fact]
    public void Unsupported_or_write_only_characteristics_are_ignored_by_legacy_selector()
    {
        Assert.Null(A2854Battery.FindReadableLevel(
            new GattService(0x0010, 0x001A, [0x12, 0x18]), [BatteryLevel]));
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
            [new(0x0011, 0x08, 0x0012, [0x19, 0x2A])]));
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
            [new(0x0011, 0x02, 0x001B, [0x19, 0x2A])]));
    }

    [Fact]
    public void Ambiguous_duplicate_battery_levels_are_not_guessed_by_legacy_selector()
    {
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
        [
            BatteryLevel,
            new(0x0014, 0x02, 0x0015, [0x19, 0x2A])
        ]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(100)]
    public void Battery_percent_uses_exact_one_byte(int level)
    {
        Assert.Equal(level, A2854Battery.DecodeLevel([(byte)level]));
    }

    [Fact]
    public void Invalid_or_undocumented_battery_values_do_not_decode_as_percent()
    {
        Assert.Null(A2854Battery.DecodeLevel([]));
        Assert.Null(A2854Battery.DecodeLevel([101]));
        Assert.Null(A2854Battery.DecodeLevel([0xFF]));
        Assert.Null(A2854Battery.DecodeLevel([70, 71]));
    }

    [Fact]
    public async Task Inspection_classifies_missing_and_ambiguous_battery_services_from_GATT()
    {
        Assert.Equal(A2854BatteryQueryOutcome.BatteryServiceMissing,
            (await Inspect([])).Outcome);
        Assert.Equal(A2854BatteryQueryOutcome.BatteryServiceAmbiguous,
            (await Inspect([BatteryService, new(0x0020, 0x002A, [0x0F, 0x18])])).Outcome);
    }

    [Fact]
    public async Task Inspection_classifies_missing_and_ambiguous_levels_from_GATT()
    {
        Assert.Equal(A2854BatteryQueryOutcome.BatteryLevelMissing,
            (await Inspect([BatteryService], [])).Outcome);
        Assert.Equal(A2854BatteryQueryOutcome.BatteryLevelAmbiguous,
            (await Inspect([BatteryService],
            [BatteryLevel, new(0x0014, 0x02, 0x0015, [0x19, 0x2A])])).Outcome);
    }

    [Fact]
    public async Task Inspection_classifies_nonreadable_level_and_propagates_rejected_read()
    {
        var noReadProperty = await Inspect([BatteryService],
            [new(0x0011, 0x08, 0x0012, [0x19, 0x2A])]);
        Assert.Equal(A2854BatteryQueryOutcome.BatteryLevelUnreadable, noReadProperty.Outcome);

        using var peer = new FakeBatteryPeer([BatteryService], [BatteryLevel], [55], rejectRead: true);
        await Assert.ThrowsAsync<AttException>(
            () => A2854Battery.InspectOnceAsync(peer.Gatt));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(45)]
    [InlineData(100)]
    public async Task Inspection_reports_successful_value_and_legacy_read_once_stays_compatible(int level)
    {
        var inspection = await Inspect([BatteryService], [BatteryLevel], [(byte)level]);
        Assert.Equal(A2854BatteryQueryOutcome.Success, inspection.Outcome);
        Assert.Equal(level, inspection.LevelPercent);

        using var peer = new FakeBatteryPeer([BatteryService], [BatteryLevel], [(byte)level]);
        Assert.Equal(level, await A2854Battery.ReadOnceAsync(peer.Gatt));
    }

    [Fact]
    public async Task Inspection_classifies_malformed_payload_without_returning_a_level()
    {
        var inspection = await Inspect([BatteryService], [BatteryLevel], [70, 71]);
        Assert.Equal(A2854BatteryQueryOutcome.InvalidPayload, inspection.Outcome);
        Assert.Null(inspection.LevelPercent);
    }

    [Fact]
    public async Task Inspection_preserves_cancellation_semantics()
    {
        using var peer = new FakeBatteryPeer([BatteryService], [BatteryLevel], [75]);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => A2854Battery.InspectOnceAsync(peer.Gatt, cancel.Token));
    }

    static async Task<A2854BatteryInspection> Inspect(GattService[] services,
        GattCharacteristic[]? characteristics = null, byte[]? value = null, bool rejectRead = false)
    {
        using var peer = new FakeBatteryPeer(services, characteristics ?? [], value ?? [55], rejectRead);
        return await A2854Battery.InspectOnceAsync(peer.Gatt);
    }

    sealed class FakeBatteryPeer : IDisposable
    {
        readonly HciHost _host;

        public GattClient Gatt { get; }

        public FakeBatteryPeer(GattService[] services, GattCharacteristic[] characteristics,
            byte[] value, bool rejectRead = false)
        {
            HciHost? host = null;
            host = new HciHost(_ => { }, sent =>
            {
                if (sent.Length < 9 || sent[6] != LeCentral.CidAtt) return;
                byte[] request = sent[8..];
                byte[] response = request[0] switch
                {
                    0x10 => FindServices(request, services),
                    0x08 => FindCharacteristics(request, characteristics),
                    0x0A when rejectRead => [0x01, 0x0A, request[1], request[2], 0x02],
                    0x0A => [0x0B, .. value],
                    _ => [0x01, request[0], request[1], request[2], 0x06]
                };
                host!.ReceiveAclBytes([
                    0x40, 0x20, (byte)(response.Length + 4), 0x00,
                    (byte)response.Length, 0x00, 0x04, 0x00, .. response
                ]);
            });
            _host = host;
            Gatt = new GattClient(new LeCentral(_host), 0x0040);
        }

        static byte[] FindServices(byte[] request, GattService[] services)
        {
            ushort start = U16(request, 1);
            GattService[] matches = services.Where(s => s.Start >= start)
                .OrderBy(s => s.Start).ToArray();
            if (matches.Length == 0) return [0x01, request[0], request[1], request[2], 0x0A];
            return [0x11, 6, .. matches.SelectMany(s => new byte[]
            {
                (byte)s.Start, (byte)(s.Start >> 8), (byte)s.End, (byte)(s.End >> 8), s.Uuid[0], s.Uuid[1]
            })];
        }

        static byte[] FindCharacteristics(byte[] request, GattCharacteristic[] characteristics)
        {
            ushort start = U16(request, 1);
            ushort end = U16(request, 3);
            GattCharacteristic[] matches = characteristics
                .Where(c => c.Declaration >= start && c.Declaration <= end)
                .OrderBy(c => c.Declaration).ToArray();
            if (matches.Length == 0) return [0x01, request[0], request[1], request[2], 0x0A];
            return [0x09, 7, .. matches.SelectMany(c => new byte[]
            {
                (byte)c.Declaration, (byte)(c.Declaration >> 8), c.Properties,
                (byte)c.Value, (byte)(c.Value >> 8), c.Uuid[0], c.Uuid[1]
            })];
        }

        static ushort U16(byte[] bytes, int offset) => (ushort)(bytes[offset] | bytes[offset + 1] << 8);

        public void Dispose() => Gatt.Detach();
    }
}
