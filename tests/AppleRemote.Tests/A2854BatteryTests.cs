// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854BatteryTests
{
    static readonly GattService BatteryService = new(0x0010, 0x001A, [0x0F, 0x18]);

    [Fact]
    public void Finds_only_unique_readable_standard_battery_level()
    {
        GattCharacteristic[] chars =
        [
            new(0x0011, 0x02, 0x0012, [0x19, 0x2A]),
            new(0x0014, 0x10, 0x0015, [0x1A, 0x2A])
        ];
        Assert.Equal((ushort)0x0012, A2854Battery.FindReadableLevel(BatteryService, chars));
    }

    [Fact]
    public void Unsupported_or_write_only_characteristics_are_ignored()
    {
        Assert.Null(A2854Battery.FindReadableLevel(
            new GattService(0x0010, 0x001A, [0x12, 0x18]),
            [new GattCharacteristic(0x0011, 0x02, 0x0012, [0x19, 0x2A])]));
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
            [new GattCharacteristic(0x0011, 0x08, 0x0012, [0x19, 0x2A])]));
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
            [new GattCharacteristic(0x0011, 0x02, 0x001B, [0x19, 0x2A])]));
    }

    [Fact]
    public void Ambiguous_duplicate_battery_levels_are_not_guessed()
    {
        Assert.Null(A2854Battery.FindReadableLevel(BatteryService,
        [
            new(0x0011, 0x02, 0x0012, [0x19, 0x2A]),
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
    public void Invalid_or_undocumented_battery_values_do_not_display_a_percentage()
    {
        Assert.Null(A2854Battery.DecodeLevel([]));
        Assert.Null(A2854Battery.DecodeLevel([101]));
        Assert.Null(A2854Battery.DecodeLevel([0xFF]));
        Assert.Null(A2854Battery.DecodeLevel([70, 71]));
    }
}
