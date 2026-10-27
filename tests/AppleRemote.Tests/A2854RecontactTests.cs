// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854RecontactTests
{
    sealed class Pointer : IRelativePointerSink
    {
        public readonly List<(int X, int Y)> Moves = [];
        public void Move(int dx, int dy) => Moves.Add((dx, dy));
        public bool LeftButton(bool down) => true;
    }

    static byte[] Report(ushort sequence, int x, byte major, byte minor, byte pressure, byte flags)
    {
        int packed = x & 0xFFF;
        return [0xFC, 0x32, (byte)sequence, (byte)(sequence >> 8), 0x01,
            (byte)packed, (byte)(packed >> 8), (byte)(packed >> 16),
            major, minor, pressure, flags];
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Decoded_release_or_hover_resets_anchor_before_recontact(bool hover, bool calibrated)
    {
        var sink = new Pointer();
        var mapper = new A2854PointerMapper(sink, 0.2);
        if (calibrated)
        {
            Assert.True(A2854TouchCalibration.TryCreate(new Dictionary<string, A2854TouchReference>
            {
                ["center"] = new(0, 0), ["left"] = new(1024, 0),
                ["right"] = new(-1024, 0), ["top"] = new(0, -1024),
                ["bottom"] = new(0, 1024)
            }, out var profile));
            mapper.Calibration = profile;
        }
        var decoder = new A2854Decoder();
        TouchEvent Feed(byte[] report)
        {
            var touch = Assert.IsType<TouchEvent>(Assert.Single(decoder.Decode(report)));
            mapper.OnEvent(touch);
            return touch;
        }

        Feed(Report(65500, 100, 0x40, 0x20, 9, 0x6C));
        Feed(Report(65530, 150, 0x40, 0x20, 9, 0x6C));
        Assert.Equal((calibrated ? -9 : 10, 0), Assert.Single(sink.Moves));
        // Includes sequence wrap and the observed lift-off shape: an ellipse
        // remains present, pressure is zero, and the hover bit is set.
        var release = hover
            ? Feed(Report(24, 50, 0x2F, 0x08, 0, 0x6E))
            : Feed(Report(24, 50, 0, 0, 0, 0x67));
        Assert.False(release.Down);
        Assert.Single(sink.Moves);

        Feed(Report(54, 50, 0x40, 0x20, 9, 0x6C));
        Assert.Single(sink.Moves); // recontact is an anchor, not a reverse move
        Feed(Report(84, 100, 0x40, 0x20, 9, 0x6C));
        Assert.Equal(new[] { (calibrated ? -9 : 10, 0), (calibrated ? -9 : 10, 0) }, sink.Moves);
    }

    [Fact]
    public void Observed_lift_off_frame_retains_geometry_but_is_not_pointer_contact()
    {
        byte[] report = Convert.FromHexString("FC324481012641E22F08006E");
        var frame = Assert.IsType<A2854TouchFrame>(A2854Decoder.ParseTouch(report.AsSpan(1)));
        Assert.Equal(1, frame.FingerCount);
        Assert.True(frame.First!.Value.Hover);
        Assert.Equal(0, frame.First.Value.Pressure);
        var decoder = new A2854Decoder();
        var touch = Assert.IsType<TouchEvent>(Assert.Single(decoder.Decode(report)));
        Assert.False(touch.Down);
        Assert.Equal(7, Assert.Single(touch.Fingers).State);
    }
}
