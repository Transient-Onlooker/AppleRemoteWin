// SPDX-License-Identifier: AGPL-3.0-or-later

using Xunit;

namespace AppleRemote.Tests;

public sealed class A2854VisualContactTrackerTests
{
    static A2854VisualContact P(int x, int y = 0) => new(x, y);

    [Fact]
    public void Second_contact_creates_new_marker_and_does_not_teleport_first()
    {
        var tracker = new A2854VisualContactTracker();
        Assert.Equal(P(-600), tracker.Update([P(-600)])[0]);
        var both = tracker.Update([P(750), P(-590)]); // input slot order reversed
        Assert.Equal(P(-590), both[0]);
        Assert.Equal(P(750), both[1]);
    }

    [Fact]
    public void Input_packet_order_can_swap_without_swapping_visual_identities()
    {
        var tracker = new A2854VisualContactTracker();
        tracker.Update([P(-900), P(900)]);
        var changed = tracker.Update([P(850), P(-860)]);
        Assert.Equal(P(-860), changed[0]);
        Assert.Equal(P(850), changed[1]);
        changed = tracker.Update([P(-810), P(810)]);
        Assert.Equal(P(-810), changed[0]);
        Assert.Equal(P(810), changed[1]);
    }

    [Fact]
    public void Lifting_either_finger_keeps_remaining_finger_in_its_original_slot()
    {
        var tracker = new A2854VisualContactTracker();
        tracker.Update([P(-600), P(700)]);
        var secondOnly = tracker.Update([P(710)]);
        Assert.Null(secondOnly[0]);
        Assert.Equal(P(710), secondOnly[1]);
        var both = tracker.Update([P(-610), P(720)]);
        Assert.Equal(P(-610), both[0]);
        Assert.Equal(P(720), both[1]);
        var firstOnly = tracker.Update([P(-600)]);
        Assert.Equal(P(-600), firstOnly[0]);
        Assert.Null(firstOnly[1]);
    }

    [Fact]
    public void Empty_frame_and_reset_discard_previous_identity()
    {
        var tracker = new A2854VisualContactTracker();
        tracker.Update([P(-800), P(900)]);
        Assert.All(tracker.Update([]), value => Assert.Null(value));
        Assert.Equal(P(500), tracker.Update([P(500)])[0]);
        tracker.Reset();
        Assert.All(tracker.Slots, value => Assert.Null(value));
    }

    [Fact]
    public void Tracking_coordinates_are_not_affected_by_touch_visual_calibration()
    {
        var tracker = new A2854VisualContactTracker();
        var frame = tracker.Update([P(-922, -675), P(422, 875)]);
        Assert.Equal(P(-922, -675), frame[0]);
        Assert.Equal(P(422, 875), frame[1]);
    }
}
