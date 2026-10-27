// SPDX-License-Identifier: AGPL-3.0-or-later
//
// Pure, testable third-generation Siri Remote pointer mapping.
// No OS calls and no BLE dependencies. The caller explicitly opts in.

namespace AppleRemote;

/// <summary>Relative Windows mouse events. Production SendInput and fake sinks share this interface.</summary>
public interface IRelativePointerSink
{
    void Move(int dx, int dy);
    /// <summary>True only when the OS accepted the button transition.</summary>
    bool LeftButton(bool down);
}

/// <summary>
/// One-contact touchpad movement and click-button state for an A2854.
/// Hover/multitouch, first touch, release and discontinuities never jump the cursor.
/// </summary>
public sealed class A2854PointerMapper(IRelativePointerSink sink, double sensitivity = 0.18)
{
    const int JumpThreshold = 350; // signed-12-bit touch coordinates; reject discontinuities
    const int MaxStep = 60;        // Windows relative pixels per accepted frame
    const double NormalizedPointerSpan = 1000; // calibrated full-axis fraction -> legacy-ish units
    // Controller-observed contact "Pressure" is empirical, NOT calibrated force.
    // A sharp rise may precede an 0xFB click; ignore only two touch frames.
    const int PressureSpikeDelta = 24;
    const int PressureGuardFrames = 2;
    const int PostClickGuardFrames = 2;
    // On a physical click, absorb finger wobble without blocking intentional
    // click-and-drag. Threshold applies to actual 12-bit touch coordinates.
    const int ClickDragDistance = 90;

    readonly IRelativePointerSink _sink = sink ?? throw new ArgumentNullException(nameof(sink));
    double _sensitivity = CheckSensitivity(sensitivity);
    bool _tracking, _held, _dragging, _clickAnchored;
    int _previousX, _previousY, _clickAnchorX, _clickAnchorY;
    byte? _previousPressure;
    int _pressureGuardRemaining, _postClickGuardRemaining;
    double _remainderX, _remainderY;
    A2854TouchCalibration? _calibration;

    /// <summary>
    /// Optional physical-axis mapping. Default is null: legacy pointer input
    /// stays byte-for-byte unchanged unless the user opts in. Updating while
    /// moving resets the previous position to prevent a sudden cursor jump.
    /// </summary>
    public A2854TouchCalibration? Calibration
    {
        get => _calibration;
        set
        {
            _calibration = value;
            ResetTracking();
        }
    }

    /// <summary>Whether a successful mouse-down still needs a confirmed mouse-up.</summary>
    public bool HasHeldClick => _held;

    /// <summary>Last requested mouse button transition was accepted by its sink.</summary>
    public bool LastButtonTransitionSucceeded { get; private set; } = true;

    public double Sensitivity
    {
        get => _sensitivity;
        set
        {
            double next = CheckSensitivity(value);
            if (next == _sensitivity) return;
            _sensitivity = next;
            // The sensitivity slider may be dragged with the A2854 pointer
            // itself. Clearing tracking on *every* value change discarded the
            // next touch frame and made live adjustment visibly stutter.
            // Preserve the previous contact and button/drag ownership; only
            // discard subpixel remainder accumulated at the old gain.
            _remainderX = _remainderY = 0;
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
                LastButtonTransitionSucceeded = _sink.LeftButton(true);
                _held = LastButtonTransitionSucceeded;
                if (_held)
                {
                    // Anchor at the last touch coordinate known BEFORE the
                    // physical click: HID touch and button frames can interleave.
                    _clickAnchored = _tracking;
                    _clickAnchorX = _previousX;
                    _clickAnchorY = _previousY;
                    _dragging = false;
                    _pressureGuardRemaining = 0;
                    _remainderX = _remainderY = 0;
                }
            }
            if (buttons.Released.Contains("click") && _held)
            {
                LastButtonTransitionSucceeded = _sink.LeftButton(false);
                _held = !LastButtonTransitionSucceeded; // failed up must remain retryable
                if (!_held)
                {
                    _dragging = _clickAnchored = false;
                    // Lift can produce a touch frame after button-up. Do not
                    // translate its wobble into a delayed cursor displacement.
                    ResetTracking();
                    _postClickGuardRemaining = PostClickGuardFrames;
                }
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

            byte? pressure = ContactPressure(touch);
            if (!_tracking)
            {
                _tracking = true;
                _previousX = touch.X;
                _previousY = touch.Y;
                _previousPressure = pressure;
                if (_held)
                {
                    _clickAnchorX = touch.X;
                    _clickAnchorY = touch.Y;
                    _clickAnchored = true;
                }
                return; // never move on first touch
            }

            int previousX = _previousX, previousY = _previousY;
            int dx = touch.X - previousX, dy = touch.Y - previousY;
            // Use radial travel, not per-axis limits: a 70/70 diagonal is
            // already a deliberate ~99-unit swipe. A new deliberate move also
            // cancels a preceding pressure spike's remaining suppression.
            bool deliberateMove = (long)dx * dx + (long)dy * dy >=
                (long)ClickDragDistance * ClickDragDistance;
            bool pressureRising = !_held && !deliberateMove && pressure.HasValue &&
                _previousPressure.HasValue &&
                pressure.Value - _previousPressure.Value >= PressureSpikeDelta;
            _previousPressure = pressure;
            if (deliberateMove) _pressureGuardRemaining = 0;
            else if (pressureRising) _pressureGuardRemaining = PressureGuardFrames;

            _previousX = touch.X;
            _previousY = touch.Y;
            if (Math.Abs(dx) > JumpThreshold || Math.Abs(dy) > JumpThreshold)
            {
                _remainderX = _remainderY = 0;
                if (_held)
                {
                    _clickAnchorX = touch.X;
                    _clickAnchorY = touch.Y;
                    _clickAnchored = true;
                }
                return; // tracking restarts at the new position, no jump
            }

            if (_held && !_dragging)
            {
                if (!_clickAnchored)
                {
                    _clickAnchorX = touch.X;
                    _clickAnchorY = touch.Y;
                    _clickAnchored = true;
                }
                long offsetX = (long)touch.X - _clickAnchorX;
                long offsetY = (long)touch.Y - _clickAnchorY;
                if (offsetX * offsetX + offsetY * offsetY <
                    (long)ClickDragDistance * ClickDragDistance)
                {
                    _remainderX = _remainderY = 0;
                    return; // physical click wobble: no cursor movement
                }
                // Deliberate movement while held becomes a drag. Do not
                // replay suppressed wobble in a sudden one-frame jump.
                _dragging = true;
                _remainderX = _remainderY = 0;
                return;
            }
            if (!_held && (_pressureGuardRemaining > 0 || _postClickGuardRemaining > 0))
            {
                if (_pressureGuardRemaining > 0) _pressureGuardRemaining--;
                if (_postClickGuardRemaining > 0) _postClickGuardRemaining--;
                _remainderX = _remainderY = 0;
                return;
            }

            double scaledX = dx, scaledY = dy;
            if (_calibration is { } profile)
            {
                var before = profile.NormalizedPosition(previousX, previousY);
                var after = profile.NormalizedPosition(touch.X, touch.Y);
                scaledX = (after.X - before.X) * NormalizedPointerSpan;
                scaledY = (after.Y - before.Y) * NormalizedPointerSpan;
            }
            double movementX = _remainderX + scaledX * _sensitivity;
            double movementY = _remainderY - scaledY * _sensitivity; // visual up => OS cursor up
            int pixelsX = Math.Clamp((int)Math.Truncate(movementX), -MaxStep, MaxStep);
            int pixelsY = Math.Clamp((int)Math.Truncate(movementY), -MaxStep, MaxStep);
            // Discard clamped excess instead of moving the mouse again on a
            // later zero-delta frame (a very visible touchpad jump bug).
            _remainderX = Math.Abs(movementX) >= MaxStep ? 0 : movementX - pixelsX;
            _remainderY = Math.Abs(movementY) >= MaxStep ? 0 : movementY - pixelsY;
            if (pixelsX != 0 || pixelsY != 0) _sink.Move(pixelsX, pixelsY);
        }
    }

    static byte? ContactPressure(TouchEvent touch)
    {
        // Normal HID reports only; never treat arbitrary gen-1 or mock event
        // buffers as force data. No raw touch data is logged or stored.
        byte[] raw = touch.Raw;
        if (raw.Length < 12 || raw[0] != A2854Decoder.TouchReportId)
            return null;
        var frame = A2854Decoder.ParseTouch(raw.AsSpan(1));
        if (frame is null) return null;
        A2854Finger? contact = frame.First is { Hover: false } first ? first
            : frame.Second is { Hover: false } second ? second
            : null;
        return contact?.Pressure;
    }

    void ResetTracking()
    {
        _tracking = false;
        _previousPressure = null;
        _pressureGuardRemaining = 0;
        _postClickGuardRemaining = 0;
        _clickAnchored = _dragging = false;
        _remainderX = _remainderY = 0;
    }

    /// <summary>
    /// Forget touch, and retry mouse-up if it was not accepted by SendInput.
    /// Never forget a held button on failed mouse-up. False means the caller
    /// must warn the user and arrange another cleanup attempt.
    /// </summary>
    public bool Reset()
    {
        ResetTracking();
        for (int attempt = 0; _held && attempt < 3; attempt++)
        {
            LastButtonTransitionSucceeded = _sink.LeftButton(false);
            _held = !LastButtonTransitionSucceeded;
        }
        return !_held;
    }
}
