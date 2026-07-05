namespace XTerm.Common;

/// <summary>
///     Identifies the active terminal buffer.
/// </summary>
public enum BufferType
{
    Normal,
    Alternate
}

/// <summary>
///     Cursor style for the terminal.
/// </summary>
public enum CursorStyle
{
    Block,
    Underline,
    Bar
}

/// <summary>
///     Character set handling modes.
/// </summary>
public enum CharsetMode
{
    G0,
    G1,
    G2,
    G3
}

/// <summary>
///     Parser state for escape sequence parsing.
/// </summary>
public enum ParserState
{
    Ground = 0,
    Escape = 1,
    EscapeIntermediate = 2,
    CsiEntry = 3,
    CsiParam = 4,
    CsiIntermediate = 5,
    CsiIgnore = 6,
    SosPmApcString = 7,
    OscString = 8,
    DcsEntry = 9,
    DcsParam = 10,
    DcsIgnore = 11,
    DcsPassthrough = 12
}

/// <summary>
///     Window information request types for OSC window queries.
/// </summary>
public enum WindowInfoRequest
{
    /// <summary>
    ///     Request window position (x, y coordinates).
    /// </summary>
    Position,

    /// <summary>
    ///     Request window size in pixels.
    /// </summary>
    SizePixels,

    /// <summary>
    ///     Request window size in characters (columns x rows).
    /// </summary>
    SizeCharacters,

    /// <summary>
    ///     Request screen size in pixels.
    /// </summary>
    ScreenSizePixels,

    /// <summary>
    ///     Request cell size in pixels.
    /// </summary>
    CellSizePixels,

    /// <summary>
    ///     Request window title.
    /// </summary>
    Title,

    /// <summary>
    ///     Request icon title.
    /// </summary>
    IconTitle,

    /// <summary>
    ///     Request window state (normal, minimized, maximized, fullscreen).
    /// </summary>
    State
}