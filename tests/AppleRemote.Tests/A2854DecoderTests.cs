// SPDX-License-Identifier: AGPL-3.0-or-later
// Regression fixtures transcribed from owner's physical A2854 / BlueZ session.
// https://github.com/Transient-Onlooker/siri-remote-for-windows/blob/main/docs/windows-port/OBSERVED_REPORT_FIXTURES.md

using Xunit;

namespace AppleRemote.Tests;

public class A2854DecoderTests
{
    static byte[] Hex(string value) => Convert.FromHexString(value.Replace(" ", ""));

    [Fact]
    public void Hardware_button_masks_are_little_endian_and_emit_transitions()
    {
        var d = new A2854Decoder();
        ButtonsEvent pressed = Assert.IsType<ButtonsEvent>(Assert.Single(d.Decode(Hex("FB 00 04"))));
        Assert.Equal(new[] { "right" }, pressed.Pressed);
        Assert.Empty(pressed.Released);
        Assert.Empty(d.Decode(Hex("FB 00 04"))); // state refresh, not a second press

        ButtonsEvent released = Assert.IsType<ButtonsEvent>(Assert.Single(d.Decode(Hex("FB 00 00"))));
        Assert.Equal(new[] { "right" }, released.Released);
        Assert.Empty(released.Pressed);

        var tv = Assert.IsType<ButtonsEvent>(Assert.Single(d.Decode(Hex("FB 01 00"))));
        Assert.Equal(new[] { "tv" }, tv.Pressed);
        var back = Assert.IsType<ButtonsEvent>(Assert.Single(d.Decode(Hex("FB 40 00"))));
        Assert.Equal(new[] { "menu" }, back.Pressed);
        Assert.Equal(new[] { "tv" }, back.Released);
    }

    [Theory]
    [InlineData("FB 02 00", "volup")]
    [InlineData("FB 04 00", "voldown")]
    [InlineData("FB 08 00", "click")]
    [InlineData("FB 10 00", "power")]
    [InlineData("FB 20 00", "siri")]
    [InlineData("FB 80 00", "mute")]
    [InlineData("FB 00 01", "playpause")]
    [InlineData("FB 00 02", "up")]
    [InlineData("FB 00 04", "right")]
    [InlineData("FB 00 08", "down")]
    [InlineData("FB 00 10", "left")]
    public void Every_known_button_has_a_stable_name(string report, string expected)
    {
        var pressed = Assert.IsType<ButtonsEvent>(
            Assert.Single(new A2854Decoder().Decode(Hex(report))));
        Assert.Equal(new[] { expected }, pressed.Pressed);
    }

    [Fact]
    public void Hardware_hover_sample_decodes_signed_coordinates_without_false_contact()
    {
        byte[] data = Hex("32 14 FC 01 C9 9E FB 51 44 01 66");
        A2854TouchFrame frame = Assert.IsType<A2854TouchFrame>(A2854Decoder.ParseTouch(data));
        var finger = Assert.IsType<A2854Finger>(frame.First);
        Assert.Equal((-311, -71), (finger.X, finger.Y));
        Assert.Equal((81, 68, 1), ((int)finger.Major, (int)finger.Minor, (int)finger.Pressure));
        Assert.True(finger.Hover);
        Assert.Equal(3, finger.AngleIndex);
        Assert.Equal(1, frame.FingerCount);

        var touch = Assert.IsType<TouchEvent>(Assert.Single(
            new A2854Decoder().Decode(Hex("FC 32 14 FC 01 C9 9E FB 51 44 01 66"))));
        Assert.False(touch.Down); // hover bit: pressure=1 does not imply a click
        Assert.Equal((-311, -71, 7), (touch.X, touch.Y, touch.Fingers[0].State));
    }

    [Fact]
    public void Hardware_contact_and_release_frames_are_distinct()
    {
        var decoder = new A2854Decoder();
        var down = Assert.IsType<TouchEvent>(Assert.Single(decoder.Decode(
            Hex("FC 32 92 D6 01 66 DC D5 60 1C 09 AC"))));
        Assert.Equal((-922, -675), (down.X, down.Y));
        Assert.True(down.Down);
        Assert.Equal(4, down.Fingers[0].State);

        var release = Assert.IsType<TouchEvent>(Assert.Single(decoder.Decode(
            Hex("FC 32 36 FC 00 C9 9E FB 00 00 00 67"))));
        Assert.False(release.Down);
        Assert.Empty(release.Fingers);
        Assert.Equal(0, A2854Decoder.ParseTouch(
            Hex("32 36 FC 00 C9 9E FB 00 00 00 67"))!.FingerCount);
    }

    [Fact]
    public void Two_finger_payload_has_separate_seven_byte_second_slot()
    {
        byte[] payload = Hex("32 92 D6 01 66 DC D5 60 1C 09 AC 01 20 30 41 22 09 00");
        var parsed = Assert.IsType<A2854TouchFrame>(A2854Decoder.ParseTouch(payload));
        Assert.Equal(2, parsed.FingerCount);
        var touch = Assert.IsType<TouchEvent>(Assert.Single(new A2854Decoder()
            .Decode([A2854Decoder.TouchReportId, .. payload])));
        Assert.Equal(2, touch.Fingers.Count);
        Assert.True(touch.Down);
    }

    [Fact]
    public void Disconnect_releases_buttons_and_active_touch()
    {
        var decoder = new A2854Decoder();
        decoder.Decode(Hex("FB 00 04"));
        decoder.Decode(Hex("FC 32 92 D6 01 66 DC D5 60 1C 09 AC"));

        var output = decoder.Decode([]);
        Assert.Collection(output,
            e => Assert.Equal(new[] { "right" },
                Assert.IsType<ButtonsEvent>(e).Released),
            e => Assert.False(Assert.IsType<TouchEvent>(e).Down));
        Assert.Empty(decoder.Decode([]));
    }

    [Fact]
    public void Malformed_packets_and_microphone_data_do_not_emit_events()
    {
        var decoder = new A2854Decoder();
        Assert.Empty(decoder.Decode(null));
        Assert.Empty(decoder.Decode(Hex("FB 00")));
        Assert.Empty(decoder.Decode(Hex("FC 31 92 D6 01 66 DC D5 60 1C 09 AC"))); // marker
        Assert.Empty(decoder.Decode(Hex("FC 32 92 D6"))); // length
        Assert.Empty(decoder.Decode(Hex("FA 01 02 03"))); // microphone
        Assert.Empty(decoder.Decode(Hex("FC 00 20"))); // gen-1 packet
    }

    [Fact]
    public void Legacy_decoder_still_handles_first_generation_reports()
    {
        var legacy = new Decoder();
        var pressed = Assert.IsType<ButtonsEvent>(Assert.Single(
            legacy.Decode(Hex("FC E0 22 60 02"))));
        Assert.Equal(new[] { "volup", "menu" }, pressed.Pressed);
    }
}
