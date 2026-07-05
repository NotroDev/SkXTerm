using XTerm.Common;

namespace XTerm.Events;

/// <summary>
///     Terminal event data and handlers.
/// </summary>
public static class TerminalEvents
{
    /// <summary>
    ///     Data event - fired when the terminal receives input data.
    /// </summary>
    public class DataEventArgs(string data) : EventArgs
    {
        public string Data { get; } = data;
    }

    /// <summary>
    ///     Resize event - fired when the terminal is resized.
    /// </summary>
    public class ResizeEventArgs(int cols, int rows) : EventArgs
    {
        public int Cols { get; } = cols;
        public int Rows { get; } = rows;
    }

    /// <summary>
    ///     Title change event - fired when the terminal title changes.
    /// </summary>
    public class TitleChangeEventArgs(string title) : EventArgs
    {
        public string Title { get; } = title;
    }

    /// <summary>
    ///     Line feed event - fired when a line feed occurs.
    /// </summary>
    public class LineFeedEventArgs(string data) : EventArgs
    {
        public string Data { get; } = data;
    }

    /// <summary>
    ///     Directory change event - fired when the current directory changes.
    /// </summary>
    public class DirectoryChangeEventArgs(string directory) : EventArgs
    {
        public string Directory { get; } = directory;
    }

    /// <summary>
    ///     Hyperlink event - fired when a hyperlink is encountered or cleared.
    /// </summary>
    public class HyperlinkEventArgs : EventArgs
    {
        public HyperlinkEventArgs(string url)
            : this(url, false)
        {
        }

        internal HyperlinkEventArgs(string url, bool isCleared)
        {
            Url = url;
            IsCleared = isCleared;
        }

        /// <summary>
        ///     Hyperlink URL. Empty when <see cref="IsCleared" /> is true.
        /// </summary>
        public string Url { get; }

        /// <summary>
        ///     True when the active hyperlink was cleared.
        /// </summary>
        public bool IsCleared { get; }
    }

    /// <summary>
    ///     Window moved event - fired when a window move command is received.
    /// </summary>
    public class WindowMovedEventArgs(int x, int y) : EventArgs
    {
        // coord in pixels
        public int X { get; } = x;
        public int Y { get; } = y;
    }

    /// <summary>
    ///     Window resized event - fired when a window resize command is received.
    /// </summary>
    public class WindowResizedEventArgs(int width, int height) : EventArgs
    {
        // width in pixels
        public int Width { get; } = width;

        // height in pixels
        public int Height { get; } = height;
    }

    /// <summary>
    ///     Window info requested event - fired when window information is requested.
    ///     The handler should set the appropriate response properties and the terminal
    ///     will automatically send the response.
    /// </summary>
    public class WindowInfoRequestedEventArgs(WindowInfoRequest request) : EventArgs
    {
        public WindowInfoRequest Request { get; } = request;

        /// <summary>
        ///     Set to true if the request was handled and a response should be sent.
        /// </summary>
        public bool Handled { get; set; }

        /// <summary>
        ///     For State request: true if window is iconified (minimized), false otherwise.
        /// </summary>
        public bool IsIconified { get; set; }

        /// <summary>
        ///     For Position request: X coordinate of window position in pixels.
        /// </summary>
        public int X { get; set; }

        /// <summary>
        ///     For Position request: Y coordinate of window position in pixels.
        /// </summary>
        public int Y { get; set; }

        /// <summary>
        ///     For SizePixels/ScreenSizePixels request: Width in pixels.
        /// </summary>
        public int WidthPixels { get; set; }

        /// <summary>
        ///     For SizePixels/ScreenSizePixels request: Height in pixels.
        /// </summary>
        public int HeightPixels { get; set; }

        /// <summary>
        ///     For CellSizePixels request: Cell width in pixels.
        /// </summary>
        public int CellWidth { get; set; }

        /// <summary>
        ///     For CellSizePixels request: Cell height in pixels.
        /// </summary>
        public int CellHeight { get; set; }

        /// <summary>
        ///     For Title/IconTitle request: The title string.
        /// </summary>
        public string? Title { get; set; }
    }

    /// <summary>
    ///     Buffer change event - fired when the active buffer switches.
    /// </summary>
    public class BufferChangedEventArgs(BufferType buffer) : EventArgs
    {
        public BufferType Buffer { get; } = buffer;
    }

    /// <summary>
    ///     Cursor style changed event - fired when cursor style or blink setting changes.
    /// </summary>
    public class CursorStyleChangedEventArgs(CursorStyle style, bool blink) : EventArgs
    {
        public CursorStyle Style { get; } = style;
        public bool Blink { get; } = blink;
    }
}