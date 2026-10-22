// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Pure, testable third-generation Siri Remote pointer mapping.
// No OS calls and no BLE dependencies. The caller explicitly opts in.

namespace AppleRemote;

/// <summary>Relative Windows mouse events. Production SendInput and fake sinks share this interface.</summary>
public interface IRelativePointerSink
{
    void Move(int dx, int dy);
    void LeftButton(bool down);
}

/// <summary>
/// One-contact touchpad movement and click-button state for an A2854.
/// Hover/multitouch, first touch, release and discontinuities never jump the cursor.
/// </summary>
public sealed class A2854PointerMapper(IRelativePointerSink sink, double sensitivity = 0.18)
{
    const int JumpThreshold = 350; // signed-12-bit touch coordinates; reject discontinuities
    const int MaxStep = 60;        // Windows relative pixels per accepted frame

    readonly IRelativePointerSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    double _sensitivity = CheckSensitivity(sensitivity);
    bool _tracking, _held;
    int _previousX, _previousY;
    double _remainderX, _remainderY;

    public double Sensitivity
    {
        get => _sensitivity;
        set
        {
            _sensitivity = CheckSensitivity(value);
            ResetTracking();
        }
    }

    static double CheckSensitivity(double value) =>
        double.IsFinite(value) && value >= 0.02 && value <= 3.0
            ? value : throw new ArgumentOutOfRangeException(nameof(value), "Sensitivity must be 0.02..3.0.");

    public void OnEvent(RemoteEvent ev)
    {
        if (ev is ButtonsEvent buttons)
        {
            if (buttons.Pressed.Contains("click") && !_held)
            {
                _held = true;
                _sink.LeftButton(true);
            }
            if (buttons.Released.Contains("click") && _held)
            {
                _held = false;
                _sink.LeftButton(false);
            }
        }
        else if (ev is TouchEvent touch)
        {
            // Only one actual contact drives the cursor. All other states
            // explicitly reset the anchor so resuming never causes a jump.
            if (!touch.Down || touch.Fingers.Count != 1 || touch.Fingers[0].State == 7)
            {
                ResetTracking();
                return;
            }

            if (!_tracking)
            {
                _tracking = true;
                _previousX = touch.X;
                _previousY = touch.Y;
                return; // never move on first touch
            }

            int dx = touch.X - _previousX, dy = touch.Y - _previousY;
            _previousX = touch.X;
            _previousY = touch.Y;
            if (Math.Abs(dx) > JumpThreshold || Math.Abs(dy) > JumpThreshold)
            {
                _remainderX = _remainderY = 0;
                return; // tracking restarts at the new position, no jump
            }

            _remainderX += dx * _sensitivity;
            _remainderY -= dy * _sensitivity; // A2854 vertical coordinate increases upward
            int pixelsX = Math.Clamp((int)Math.Truncate(_remainderX), -MaxStep, MaxStep);
            int pixelsY = Math.Clamp((int)Math.Truncate(_remainderY), -MaxStep, MaxStep);
            _remainderX -= pixelsX;
            _remainderY -= pixelsY;
            if (pixelsX != 0 || pixelsY != 0) _sink.Move(pixelsX, pixelsY);
        }
    }

    void ResetTracking()
    {
        _tracking = false;
        _remainderX = _remainderY = 0;
    }

    /// <summary>Releases a held click and forgets touch on disconnect, stop or opt-out.</summary>
    public void Reset()
    {
        ResetTracking();
        if (_held)
        {
            _held = false;
            _sink.LeftButton(false);
        }
    }
}
