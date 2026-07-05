using XTerm.Buffer;
using XTerm.Common;
using XTerm.Events;
using XTerm.Events.Parser;
using XTerm.Input;
using XTerm.Options;
using XTerm.Parser;
using XTerm.Selection;

namespace XTerm;

/// <summary>
///     Main terminal class - the core of xterm.js functionality.
///     Manages buffer, parser, input handler, and terminal state.
/// </summary>
public class Terminal
{
    private readonly TerminalBuffer? _altBuffer;
    private readonly InputHandler _inputHandler;
    private readonly KeyboardInputGenerator _keyboardInput;
    private readonly MouseTracker _mouseTracker;
    private readonly TerminalBuffer? _normalBuffer;
    private readonly EscapeSequenceParser _parser;

    public Terminal(TerminalOptions? options = null)
    {
        Options = options ?? new TerminalOptions();
        Cols = Options.Cols;
        Rows = Options.Rows;
        Title = string.Empty;

        // Initialize buffers
        _normalBuffer = new TerminalBuffer(Cols, Rows, Options.Scrollback);
        _altBuffer = new TerminalBuffer(Cols, Rows, 0); // Alt buffer has no scrollback
        Buffer = _normalBuffer;
        IsAlternateBufferActive = false;

        // Initialize parser and input handler
        _parser = new EscapeSequenceParser();
        _inputHandler = new InputHandler(this);
        _keyboardInput = new KeyboardInputGenerator(this);
        _mouseTracker = new MouseTracker();
        Selection = new SelectionManager(this);

        // Subscribe to parser events using C# event pattern
        _parser.Print += OnParserPrint;
        _parser.Execute += OnParserExecute;
        _parser.Csi += OnParserCsi;
        _parser.Esc += OnParserEsc;
        _parser.Osc += OnParserOsc;

        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        SendFocusEvents = false;
    }

    public TerminalOptions Options { get; }
    public TerminalBuffer Buffer { get; private set; }

    public int Cols { get; private set; }
    public int Rows { get; private set; }
    public BufferType ActiveBuffer => IsAlternateBufferActive ? BufferType.Alternate : BufferType.Normal;
    public bool IsAlternateBufferActive { get; private set; }

    // Terminal state
    public bool InsertMode { get; set; }
    public bool ApplicationCursorKeys { get; set; }
    public bool ApplicationKeypad { get; set; }
    public bool BracketedPasteMode { get; set; }
    public bool OriginMode { get; set; }
    public bool CursorVisible { get; set; }
    public bool ReverseWraparound { get; set; }
    public bool ReverseVideo { get; set; }
    public bool SendFocusEvents { get; set; }
    public bool Win32InputMode { get; set; }

    /// <summary>
    ///     When enabled, the eighth bit of input characters is used for Meta key.
    ///     Mode 1034 (eightBitInput).
    /// </summary>
    public bool EightBitInput { get; set; }

    /// <summary>
    ///     When enabled, pressing Meta+key sends ESC followed by the key.
    ///     Mode 1036 (metaSendsEscape).
    /// </summary>
    public bool MetaSendsEscape { get; set; }

    /// <summary>
    ///     When enabled, pressing Alt+key sends ESC followed by the key.
    ///     Mode 1039 (altSendsEscape).
    /// </summary>
    public bool AltSendsEscape { get; set; }

    public string Title { get; set; }
    public string? CurrentDirectory { get; set; }
    public string? CurrentHyperlink { get; set; }
    public string? HyperlinkId { get; set; }

    /// <summary>
    ///     Gets the current mouse tracking mode.
    /// </summary>
    public MouseTrackingMode MouseTrackingMode => _mouseTracker.TrackingMode;

    /// <summary>
    ///     Gets the current mouse encoding format.
    /// </summary>
    public MouseEncoding MouseEncoding => _mouseTracker.Encoding;

    /// <summary>
    ///     Gets the selection manager for text selection.
    /// </summary>
    public SelectionManager Selection { get; }

    /// <summary>
    ///     Fired when the cursor style or blink setting changes.
    /// </summary>
    public event EventHandler<TerminalEvents.CursorStyleChangedEventArgs>? CursorStyleChanged;

    // Events - Standard C# EventHandler pattern
    /// <summary>
    ///     Fired when the terminal wants to send data back to the application.
    /// </summary>
    public event EventHandler<TerminalEvents.DataEventArgs>? DataReceived;

    /// <summary>
    ///     Fired when the terminal title changes.
    /// </summary>
    public event EventHandler<TerminalEvents.TitleChangeEventArgs>? TitleChanged;

    /// <summary>
    ///     Fired when the terminal bell is activated.
    /// </summary>
    public event EventHandler? BellRang;

    /// <summary>
    ///     Fired when the terminal is resized.
    /// </summary>
    public event EventHandler<TerminalEvents.ResizeEventArgs>? Resized;

    /// <summary>
    ///     Fired when the viewport scrolls.
    /// </summary>
    public event EventHandler? Scrolled;

    /// <summary>
    ///     Fired when a line feed occurs.
    /// </summary>
    public event EventHandler<TerminalEvents.LineFeedEventArgs>? LineFed;

    /// <summary>
    ///     Fired when the current directory changes.
    /// </summary>
    public event EventHandler<TerminalEvents.DirectoryChangeEventArgs>? DirectoryChanged;

    /// <summary>
    ///     Fired when a hyperlink is encountered.
    /// </summary>
    public event EventHandler<TerminalEvents.HyperlinkEventArgs>? HyperlinkChanged;

    // Window manipulation events
    /// <summary>
    ///     Fired when a window move command is received.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowMovedEventArgs>? WindowMoved;

    /// <summary>
    ///     Fired when a window resize command is received.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowResizedEventArgs>? WindowResized;

    /// <summary>
    ///     Fired when a window minimize command is received.
    /// </summary>
    public event EventHandler? WindowMinimized;

    /// <summary>
    ///     Fired when a window maximize command is received.
    /// </summary>
    public event EventHandler? WindowMaximized;

    /// <summary>
    ///     Fired when a window restore command is received.
    /// </summary>
    public event EventHandler? WindowRestored;

    /// <summary>
    ///     Fired when a window raise command is received.
    /// </summary>
    public event EventHandler? WindowRaised;

    /// <summary>
    ///     Fired when a window lower command is received.
    /// </summary>
    public event EventHandler? WindowLowered;

    /// <summary>
    ///     Fired when a window refresh command is received.
    /// </summary>
    public event EventHandler? WindowRefreshed;

    /// <summary>
    ///     Fired when a window fullscreen command is received.
    /// </summary>
    public event EventHandler? WindowFullscreened;

    /// <summary>
    ///     Fired when window information is requested.
    /// </summary>
    public event EventHandler<TerminalEvents.WindowInfoRequestedEventArgs>? WindowInfoRequested;

    /// <summary>
    ///     Fired when the active buffer is changed.
    /// </summary>
    public event EventHandler<TerminalEvents.BufferChangedEventArgs>? BufferChanged;

    /// <summary>
    ///     Handles print events from the parser.
    /// </summary>
    private void OnParserPrint(object? sender, PrintEventArgs e)
    {
        _inputHandler.Print(e.Data);
    }

    /// <summary>
    ///     Handles execute events from the parser.
    /// </summary>
    private void OnParserExecute(object? sender, ExecuteEventArgs e)
    {
        HandleExecute(e.Code);
    }

    /// <summary>
    ///     Handles CSI events from the parser.
    /// </summary>
    private void OnParserCsi(object? sender, CsiEventArgs e)
    {
        _inputHandler.HandleCsi(e.Identifier, e.Parameters);
    }

    /// <summary>
    ///     Handles ESC events from the parser.
    /// </summary>
    private void OnParserEsc(object? sender, EscEventArgs e)
    {
        _inputHandler.HandleEsc(e.FinalChar, e.Collected);
    }

    /// <summary>
    ///     Handles OSC events from the parser.
    /// </summary>
    private void OnParserOsc(object? sender, OscEventArgs e)
    {
        _inputHandler.HandleOsc(e.Data);
    }

    /// <summary>
    ///     Writes data to the terminal.
    /// </summary>
    public void Write(string data)
    {
        if (string.IsNullOrEmpty(data))
        {
            return;
        }

        _parser.Parse(data);
    }

    /// <summary>
    ///     Writes data to the terminal as a line (adds line feed).
    /// </summary>
    public void WriteLine(string data)
    {
        Write(data + "\r\n");
    }

    /// <summary>
    ///     Resizes the terminal.
    /// </summary>
    public void Resize(int cols, int rows)
    {
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        Cols = cols;
        Rows = rows;

        // Resize buffers
        _normalBuffer?.Resize(cols, rows);
        _altBuffer?.Resize(cols, rows);

        Resized?.Invoke(this, new TerminalEvents.ResizeEventArgs(cols, rows));
    }

    /// <summary>
    ///     Resets the terminal to initial state.
    /// </summary>
    public void Reset()
    {
        // Reset to normal buffer
        if (IsAlternateBufferActive)
        {
            Buffer = _normalBuffer!;
            IsAlternateBufferActive = false;
            _inputHandler.SetBuffer(Buffer);
        }

        // Reset parser
        _parser.Reset();

        // Reset modes
        InsertMode = false;
        ApplicationCursorKeys = false;
        ApplicationKeypad = false;
        BracketedPasteMode = false;
        OriginMode = false;
        CursorVisible = true;
        ReverseWraparound = false;
        ReverseVideo = false;
        SendFocusEvents = false;
        EightBitInput = false;
        MetaSendsEscape = false; // Default is disabled
        AltSendsEscape = false;
        Win32InputMode = false;

        // Reset cursor
        Buffer.SetCursor(0, 0);
        Buffer.ResetScrollRegion();

        // Clear buffers
        ClearBuffer();
    }

    /// <summary>
    ///     Clears the entire buffer.
    /// </summary>
    public void Clear()
    {
        ClearBuffer();
    }

    private void ClearBuffer()
    {
        // Clear all lines in the buffer (including scrollback)
        // and reset line attributes (double-width/double-height) to normal
        for (int i = 0; i < Buffer.Lines.Length; i++)
        {
            BufferLine? line = Buffer.Lines[i];
            if (line != null)
            {
                line.Fill(BufferCell.Space);
                line.LineAttribute = LineAttribute.Normal;
            }
        }

        Buffer.SetCursor(0, 0);
    }

    /// <summary>
    ///     Scrolls the viewport by a specified number of lines.
    /// </summary>
    public void ScrollLines(int lines)
    {
        Buffer.ScrollDisp(lines);
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Scrolls the viewport to the top.
    /// </summary>
    public void ScrollToTop()
    {
        Buffer.ScrollToTop();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Scrolls the viewport to the bottom.
    /// </summary>
    public void ScrollToBottom()
    {
        Buffer.ScrollToBottom();
        Scrolled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Gets the content of a line as a string.
    /// </summary>
    public string GetLine(int line)
    {
        if (line < 0 || line >= Buffer.Lines.Length)
        {
            return string.Empty;
        }

        BufferLine? bufferLine = Buffer.Lines[line];
        return bufferLine?.TranslateToString(true) ?? string.Empty;
    }

    /// <summary>
    ///     Gets all visible lines as strings.
    /// </summary>
    public string[] GetVisibleLines()
    {
        string[] lines = new string[Rows];
        for (int i = 0; i < Rows; i++)
        {
            lines[i] = GetLine(Buffer.YDisp + i);
        }

        return lines;
    }

    /// <summary>
    ///     Generates an escape sequence for a key press.
    /// </summary>
    /// <param name="key">The key that was pressed</param>
    /// <param name="modifiers">Modifier keys (Shift, Alt, Control)</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateKeyInput(Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _keyboardInput.GenerateKeySequence(key, modifiers);
    }

    /// <summary>
    ///     Generates an escape sequence for a character with modifiers.
    /// </summary>
    /// <param name="c">The character that was typed</param>
    /// <param name="modifiers">Modifier keys (Shift, Alt, Control)</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateCharInput(char c, KeyModifiers modifiers = KeyModifiers.None)
    {
        return _keyboardInput.GenerateCharSequence(c, modifiers);
    }

    /// <summary>
    ///     Generates an escape sequence for a mouse event.
    /// </summary>
    /// <param name="button">The mouse button</param>
    /// <param name="x">The column position (0-based)</param>
    /// <param name="y">The row position (0-based)</param>
    /// <param name="eventType">The type of mouse event</param>
    /// <param name="modifiers">Modifier keys held during the event</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateMouseEvent(MouseButton button, int x, int y, MouseEventType eventType,
        KeyModifiers modifiers = KeyModifiers.None)
    {
        return _mouseTracker.GenerateMouseEvent(button, x, y, eventType, modifiers);
    }

    /// <summary>
    ///     Generates an escape sequence for a focus event (focus in/out).
    /// </summary>
    /// <param name="focused">True if focused, false if lost focus</param>
    /// <returns>The escape sequence string to send to the application</returns>
    public string GenerateFocusEvent(bool focused)
    {
        return _mouseTracker.GenerateFocusEvent(focused);
    }

    /// <summary>
    ///     Gets the mouse tracker (internal use for mode setting).
    /// </summary>
    internal MouseTracker GetMouseTracker()
    {
        return _mouseTracker;
    }

    // Internal methods for raising events (called by InputHandler)
    internal void RaiseDataReceived(string data)
    {
        DataReceived?.Invoke(this, new TerminalEvents.DataEventArgs(data));
    }

    internal void RaiseTitleChanged(string title)
    {
        TitleChanged?.Invoke(this, new TerminalEvents.TitleChangeEventArgs(title));
    }

    internal void RaiseDirectoryChanged(string directory)
    {
        DirectoryChanged?.Invoke(this, new TerminalEvents.DirectoryChangeEventArgs(directory));
    }

    internal void RaiseHyperlinkChanged(string? url)
    {
        HyperlinkChanged?.Invoke(this, new TerminalEvents.HyperlinkEventArgs(url ?? string.Empty, url == null));
    }

    internal void RaiseWindowMoved(int x, int y)
    {
        WindowMoved?.Invoke(this, new TerminalEvents.WindowMovedEventArgs(x, y));
    }

    internal void RaiseWindowResized(int width, int height)
    {
        WindowResized?.Invoke(this, new TerminalEvents.WindowResizedEventArgs(width, height));
    }

    internal void RaiseWindowMinimized()
    {
        WindowMinimized?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowMaximized()
    {
        WindowMaximized?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowRestored()
    {
        WindowRestored?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowRaised()
    {
        WindowRaised?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowLowered()
    {
        WindowLowered?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowRefreshed()
    {
        WindowRefreshed?.Invoke(this, EventArgs.Empty);
    }

    internal void RaiseWindowFullscreened()
    {
        WindowFullscreened?.Invoke(this, EventArgs.Empty);
    }

    internal TerminalEvents.WindowInfoRequestedEventArgs RaiseWindowInfoRequested(WindowInfoRequest request)
    {
        TerminalEvents.WindowInfoRequestedEventArgs args = new(request);
        WindowInfoRequested?.Invoke(this, args);
        return args;
    }

    /// <summary>
    ///     Updates cursor style and blink settings and notifies listeners if changed.
    /// </summary>
    /// <param name="style">Cursor rendering style.</param>
    /// <param name="blink">Whether the cursor should blink.</param>
    public void SetCursorStyle(CursorStyle style, bool blink)
    {
        bool changed = Options.CursorStyle != style || Options.CursorBlink != blink;
        Options.CursorStyle = style;
        Options.CursorBlink = blink;

        if (changed)
        {
            CursorStyleChanged?.Invoke(this, new TerminalEvents.CursorStyleChangedEventArgs(style, blink));
        }
    }

    /// <summary>
    ///     Switches to the alternate buffer.
    /// </summary>
    public void SwitchToAltBuffer()
    {
        if (IsAlternateBufferActive)
        {
            return;
        }

        Buffer = _altBuffer!;
        IsAlternateBufferActive = true;
        _inputHandler.SetBuffer(Buffer);
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Alternate));
    }

    /// <summary>
    ///     Switches to the normal buffer.
    /// </summary>
    public void SwitchToNormalBuffer()
    {
        if (!IsAlternateBufferActive)
        {
            return;
        }

        Buffer = _normalBuffer!;
        IsAlternateBufferActive = false;
        _inputHandler.SetBuffer(Buffer);
        BufferChanged?.Invoke(this, new TerminalEvents.BufferChangedEventArgs(BufferType.Normal));
    }

    /// <summary>
    ///     Handles C0 control characters.
    /// </summary>
    private void HandleExecute(int code)
    {
        switch (code)
        {
            case 0x07: // BEL
                BellRang?.Invoke(this, EventArgs.Empty);
                break;

            case 0x08: // BS - Backspace
                if (Buffer.X > 0)
                {
                    Buffer.SetCursor(Buffer.X - 1, Buffer.Y);
                }

                break;

            case 0x09: // HT - Tab
            {
                int nextTabStop = (Buffer.X + 8) / 8 * 8;
                Buffer.SetCursor(Math.Min(nextTabStop, Cols - 1), Buffer.Y);
            }
                break;

            case 0x0A: // LF - Line Feed
            case 0x0B: // VT - Vertical Tab
            case 0x0C: // FF - Form Feed
                LineFeed();
                break;

            case 0x0D: // CR - Carriage Return
                Buffer.SetCursor(0, Buffer.Y);
                break;

            case 0x0E: // SO - Shift Out (select G1 charset)
                _inputHandler.ShiftOut();
                break;

            case 0x0F: // SI - Shift In (select G0 charset)
                _inputHandler.ShiftIn();
                break;
        }
    }

    /// <summary>
    ///     Performs a line feed operation.
    /// </summary>
    private void LineFeed()
    {
        if (Buffer.Y == Buffer.ScrollBottom)
        {
            // Scroll up
            Buffer.ScrollUp(1);
        }
        else
        {
            // Move cursor down
            Buffer.SetCursor(Buffer.X, Buffer.Y + 1);
        }

        // If ConvertEol is enabled, also do a carriage return (move to column 0)
        if (Options.ConvertEol)
        {
            Buffer.SetCursor(0, Buffer.Y);
        }

        LineFed?.Invoke(this, new TerminalEvents.LineFeedEventArgs("\n"));
    }

    /// <summary>
    ///     Disposes the terminal and releases resources.
    /// </summary>
    public void Dispose()
    {
        // Unsubscribe from parser events
        _parser.Print -= OnParserPrint;
        _parser.Execute -= OnParserExecute;
        _parser.Csi -= OnParserCsi;
        _parser.Esc -= OnParserEsc;
        _parser.Osc -= OnParserOsc;

        // Clear all event subscriptions
        DataReceived = null;
        TitleChanged = null;
        BellRang = null;
        Resized = null;
        Scrolled = null;
        LineFed = null;
        DirectoryChanged = null;
        HyperlinkChanged = null;

        // Clear window manipulation events
        WindowMoved = null;
        WindowResized = null;
        WindowMinimized = null;
        WindowMaximized = null;
        WindowRestored = null;
        WindowRaised = null;
        WindowLowered = null;
        WindowRefreshed = null;
        WindowFullscreened = null;
        WindowInfoRequested = null;
    }

    /// <summary>
    ///     Clears the scrollback buffer for both normal and alternate buffers.
    /// </summary>
    public void ClearScrollback()
    {
        _normalBuffer?.ClearScrollback();
        _altBuffer?.ClearScrollback();

        _inputHandler.SetBuffer(Buffer);
    }
}