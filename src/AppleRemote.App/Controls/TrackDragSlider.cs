// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace AppleRemote.App.Controls;

/// <summary>
/// A normal bound WPF Slider whose track supports both click-to-value AND
/// press-and-drag from anywhere on the track, not just from the tiny Thumb.
/// Native pointer/keyboard events are not synthesized by this control.
/// </summary>
public sealed class TrackDragSlider : Slider
{
    bool _trackDragging;

    Track? PartTrack => Template.FindName("PART_Track", this) as Track;

    bool UpdateFromPointer(MouseEventArgs e)
    {
        var track = PartTrack;
        if (track is null) return false;
        double value = track.ValueFromPoint(e.GetPosition(track));
        if (!double.IsFinite(value)) return false;
        // SetCurrentValue preserves the VM's two-way binding.
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
        return true;
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var track = PartTrack;
        if (!IsEnabled || track?.Thumb is null || track.Thumb.IsMouseOver)
        {
            base.OnPreviewMouseLeftButtonDown(e); // Thumb keeps native drag behavior
            return;
        }

        if (!UpdateFromPointer(e))
        {
            base.OnPreviewMouseLeftButtonDown(e);
            return;
        }

        Focus();
        _trackDragging = CaptureMouse();
        e.Handled = true; // do not invoke Slider's default large-step action
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_trackDragging)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                EndTrackDrag();
                return;
            }
            UpdateFromPointer(e);
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_trackDragging)
        {
            UpdateFromPointer(e);
            EndTrackDrag();
            e.Handled = true;
            return;
        }
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _trackDragging = false;
        base.OnLostMouseCapture(e);
    }

    void EndTrackDrag()
    {
        _trackDragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }
}
