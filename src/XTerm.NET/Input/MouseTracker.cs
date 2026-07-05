namespace XTerm.Input;

/// <summary>
///     Tracks mouse state and generates mouse event sequences.
/// </summary>
public class MouseTracker
{
    private bool _isButtonDown;
    private MouseButton _lastButton = MouseButton.None;

    // Mouse modes
    public MouseTrackingMode TrackingMode { get; set; } = MouseTrackingMode.None;
    public MouseEncoding Encoding { get; set; } = MouseEncoding.Default;
    public bool FocusEvents { get; set; }

    /// <summary>
    ///     Generates a mouse event sequence.
    /// </summary>
    public string GenerateMouseEvent(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        // Check if this mode supports this event type
        if (!ShouldReportEvent(eventType))
        {
            return string.Empty;
        }

        // Update state
        UpdateState(button, eventType);

        // Generate sequence based on encoding
        return Encoding switch
        {
            MouseEncoding.Sgr => GenerateSgrSequence(button, x, y, eventType, modifiers),
            MouseEncoding.Urxvt => GenerateUrxvtSequence(button, x, y, eventType, modifiers),
            MouseEncoding.Utf8 => GenerateUtf8Sequence(button, x, y, eventType, modifiers),
            _ => GenerateDefaultSequence(button, x, y, eventType, modifiers)
        };
    }

    /// <summary>
    ///     Generates a focus event sequence.
    /// </summary>
    public string GenerateFocusEvent(bool focused)
    {
        if (!FocusEvents)
        {
            return string.Empty;
        }

        return focused ? "\e[I" : "\e[O";
    }

    private bool ShouldReportEvent(MouseEventType eventType)
    {
        if (TrackingMode == MouseTrackingMode.None)
        {
            return false;
        }

        return TrackingMode switch
        {
            MouseTrackingMode.X10 => eventType == MouseEventType.Down,
            MouseTrackingMode.Vt200 => eventType == MouseEventType.Down || eventType == MouseEventType.Up ||
                                       eventType == MouseEventType.WheelUp || eventType == MouseEventType.WheelDown,
            MouseTrackingMode.ButtonEvent => eventType != MouseEventType.Move,
            MouseTrackingMode.AnyEvent => true,
            _ => false
        };
    }

    private void UpdateState(MouseButton button, MouseEventType eventType)
    {
        switch (eventType)
        {
            case MouseEventType.Down:
                _lastButton = button;
                _isButtonDown = true;
                break;
            case MouseEventType.Up:
                _isButtonDown = false;
                break;
        }
    }

    private string GenerateDefaultSequence(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers)
    {
        // X10/VT200 format: ESC [ M Cb Cx Cy
        // Where Cb, Cx, Cy are encoded as value + 32 (to make printable ASCII)

        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1 + 32; // 1-based + 32 offset
        int cy = y + 1 + 32; // 1-based + 32 offset

        // Clamp to valid range (32-255)
        cx = Math.Clamp(cx, 32, 255);
        cy = Math.Clamp(cy, 32, 255);

        return $"\e[M{(char)cb}{(char)cx}{(char)cy}";
    }

    private string GenerateUtf8Sequence(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers)
    {
        // Similar to default but uses UTF-8 encoding for coordinates > 223
        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1 + 32;
        int cy = y + 1 + 32;

        return $"\e[M{(char)cb}{EncodeUtf8Coord(cx)}{EncodeUtf8Coord(cy)}";
    }

    private string GenerateSgrSequence(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers)
    {
        // SGR format: ESC [ < Cb ; Cx ; Cy M/m
        // M for button press, m for button release
        // No encoding offset, coordinates are decimal numbers (1-based)

        int cb = EncodeButtonSgr(button, eventType, modifiers);
        int cx = x + 1; // 1-based
        int cy = y + 1; // 1-based

        char terminator = eventType == MouseEventType.Up ? 'm' : 'M';

        return $"\e[<{cb};{cx};{cy}{terminator}";
    }

    private string GenerateUrxvtSequence(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers)
    {
        // URXVT format: ESC [ Cb ; Cx ; Cy M
        int cb = EncodeButtonDefault(button, eventType, modifiers);
        int cx = x + 1; // 1-based
        int cy = y + 1; // 1-based

        return $"\e[{cb};{cx};{cy}M";
    }

    /// <summary>
    ///     Encodes button for X10/VT200/UTF8/URXVT formats (includes +32 base).
    /// </summary>
    private int EncodeButtonDefault(MouseButton button, MouseEventType eventType, KeyModifiers modifiers)
    {
        int cb = 32; // Base value for X10/VT200

        switch (button)
        {
            // Button encoding
            case MouseButton.WheelUp:
                cb += 64;
                break;
            case MouseButton.WheelDown:
                cb += 65;
                break;
            default:
            {
                switch (eventType)
                {
                    case MouseEventType.Move or MouseEventType.Drag:
                    {
                        // Motion events
                        cb += 32; // Motion flag
                        if (_isButtonDown)
                        {
                            cb += (int)_lastButton;
                        }
                        else
                        {
                            cb += 3; // No button (for move without button down)
                        }

                        break;
                    }
                    case MouseEventType.Up:
                        // Release - button 3 (no button info in X10/VT200)
                        cb += 3;
                        break;
                    default:
                        // Button down
                        cb += (int)button;
                        break;
                }

                break;
            }
        }

        // Add modifier flags
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            cb += 4;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            cb += 8;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            cb += 16;
        }

        return cb;
    }

    /// <summary>
    ///     Encodes button for SGR format (no +32 base, preserves button info on release).
    /// </summary>
    private int EncodeButtonSgr(MouseButton button, MouseEventType eventType, KeyModifiers modifiers)
    {
        int cb; // No base offset for SGR

        switch (button)
        {
            // Button encoding
            case MouseButton.WheelUp:
                cb = 64;
                break;
            case MouseButton.WheelDown:
                cb = 65;
                break;
            default:
            {
                switch (eventType)
                {
                    case MouseEventType.Move or MouseEventType.Drag:
                    {
                        // Motion events - add motion flag (32)
                        cb = 32;
                        if (_isButtonDown && _lastButton != MouseButton.None)
                        {
                            cb += (int)_lastButton;
                        }
                        else
                        {
                            cb += 3; // No button pressed during move
                        }

                        break;
                    }
                    case MouseEventType.Up:
                        // SGR preserves button info on release (terminator 'm' indicates release)
                        cb = (int)button;
                        break;
                    default:
                        // Button down
                        cb = (int)button;
                        break;
                }

                break;
            }
        }

        // Add modifier flags
        if ((modifiers & KeyModifiers.Shift) != 0)
        {
            cb += 4;
        }

        if ((modifiers & KeyModifiers.Alt) != 0)
        {
            cb += 8;
        }

        if ((modifiers & KeyModifiers.Control) != 0)
        {
            cb += 16;
        }

        return cb;
    }

    private string EncodeUtf8Coord(int value)
    {
        return value < 128
            ? ((char)value).ToString()
            // UTF-8 encoding for values >= 128
            // This is simplified - proper UTF-8 encoding for coordinates
            : char.ConvertFromUtf32(value);
    }

    /// <summary>
    ///     Resets mouse tracking state.
    /// </summary>
    public void Reset()
    {
        TrackingMode = MouseTrackingMode.None;
        Encoding = MouseEncoding.Default;
        FocusEvents = false;
        _lastButton = MouseButton.None;
        _isButtonDown = false;
    }
}