using System.Diagnostics;
using System.Text;
using NeoSmart.Unicode;
using Wcwidth;
using XTerm.Buffer;
using XTerm.Common;
using XTerm.Events;
using XTerm.Input;
using XTerm.Parser;

namespace XTerm;

/// <summary>
///     Handles input escape sequences and updates the terminal buffer.
///     Implements VT100/xterm escape sequence handlers.
/// </summary>
public class InputHandler(Terminal terminal)
{
    // Variation selector and combining character constants
    private const int VARIATION_SELECTOR_EMOJI_SYMBOL = 0xFE0F; // Emoji presentation selector
    private const int VARIATION_SELECTOR_TEXT_SYMBOL = 0xFE0E; // Text presentation selector
    private const int ZERO_WIDTH_JOINER = 0x200D; // ZWJ for emoji sequences
    private readonly Dictionary<CharsetMode, Dictionary<char, string>?> _charsets = new()
    {
        { CharsetMode.G0, Charsets.Ascii },
        { CharsetMode.G1, Charsets.Ascii },
        { CharsetMode.G2, Charsets.Ascii },
        { CharsetMode.G3, Charsets.Ascii }
    };

    private TerminalBuffer _buffer = terminal.Buffer;
    private AttributeData _curAttr = AttributeData.Default;
    private CharsetMode _currentCharset = CharsetMode.G0; // G0 is active by default

    // Initialize charset tables - all start as ASCII

    /// <summary>
    ///     Checks if a code point is a combining character that should be merged with the previous cell.
    /// </summary>
    private static bool IsCombiningCharacter(int codePoint)
    {
        switch (codePoint)
        {
            // Variation Selectors (U+FE00�U+FE0F)
            case >= 0xFE00 and <= 0xFE0F:
            // Variation Selectors Supplement (U+E0100�U+E01EF)
            case >= 0xE0100 and <= 0xE01EF:
            // Zero Width Joiner (U+200D)
            case ZERO_WIDTH_JOINER:
            // Combining Diacritical Marks (U+0300�U+036F)
            case >= 0x0300 and <= 0x036F:
            // Combining Diacritical Marks Extended (U+1AB0�U+1AFF)
            case >= 0x1AB0 and <= 0x1AFF:
            // Combining Diacritical Marks Supplement (U+1DC0�U+1DFF)
            case >= 0x1DC0 and <= 0x1DFF:
            // Combining Diacritical Marks for Symbols (U+20D0�U+20FF)
            case >= 0x20D0 and <= 0x20FF:
            // Combining Half Marks (U+FE20�U+FE2F)
            case >= 0xFE20 and <= 0xFE2F:
            // Emoji Modifiers / Skin Tones (U+1F3FB�U+1F3FF)
            case >= 0x1F3FB and <= 0x1F3FF:
                return true;
        }

        // Keycap combining sequence (U+20E3)
        return codePoint == 0x20E3;
    }

    /// <summary>
    ///     Prints a character to the buffer.
    /// </summary>
    public void Print(string data)
    {
        // Check if this is a combining character that should be merged with the previous cell
        if (!string.IsNullOrEmpty(data))
        {
            int codePoint = char.ConvertToUtf32(data, 0);

            if (IsCombiningCharacter(codePoint))
            {
                // Find the previous cell to combine with
                if (TryAppendToPreviousCell(data, codePoint))
                {
                    return; // Successfully combined, don't create new cell
                }
                // If we can't combine (e.g., at start of line), fall through to normal handling
            }
        }


        // Handle autowrap
        if (_buffer.X >= terminal.Cols)
        {
            if (terminal.Options.Wraparound)
            {
                if (_buffer.Y == _buffer.ScrollBottom)
                {
                    _buffer.SetCursor(0, _buffer.Y);
                    _buffer.ScrollUp(1, true);
                }
                else
                {
                    _buffer.SetCursor(0, _buffer.Y + 1);
                }

                _buffer.Lines[_buffer.Y + _buffer.YBase]!.IsWrapped = true;
            }
            else
            {
                return; // Don't print beyond line edge
            }
        }

        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return;
        }

        // Translate character through active charset
        string translatedData = data;
        if (data.Length == 1)
        {
            Dictionary<char, string>? charset = _charsets.GetValueOrDefault(_currentCharset);
            translatedData = Charsets.TranslateChar(data[0], charset);
        }

        // Get character width
        int width = GetStringCellWidth(translatedData);

        // Create cell
        BufferCell cell = new()
        {
            Content = translatedData,
            Width = width,
            Attributes = _curAttr,
            CodePoint = translatedData.Length > 0 ? char.ConvertToUtf32(translatedData, 0) : 0
        };

        // Insert mode handling
        if (terminal.InsertMode)
        {
            // Shift cells right
            line.CopyCellsFrom(line, _buffer.X, _buffer.X + width, terminal.Cols - _buffer.X - width, false);
        }

        // Set the cell
        line.SetCell(_buffer.X, ref cell);

        // Handle wide characters
        if (width == 2)
        {
            // Set following cell as a spacer
            if (_buffer.X + 1 < terminal.Cols)
            {
                BufferCell spacer = BufferCell.Empty;
                spacer.Attributes = _curAttr;
                line.SetCell(_buffer.X + 1, ref spacer);
            }
        }

        // Use MoveCursor to allow X to be one past the last column (pending wrap)
        _buffer.SetCursorRaw(_buffer.X + width, _buffer.Y);
    }

    /// <summary>
    ///     Attempts to append a combining character to the previous cell.
    /// </summary>
    /// <param name="data">The combining character string.</param>
    /// <param name="codePoint">The code point of the combining character.</param>
    /// <returns>True if successfully combined, false otherwise.</returns>
    private bool TryAppendToPreviousCell(string data, int codePoint)
    {
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return false;
        }

        // Find the previous cell position
        int prevX = _buffer.X - 1;

        // If we're at the start of a line, we might need to look at the previous line
        if (prevX < 0)
        {
            // Check if the previous line exists and is wrapped
            if (_buffer.Y > 0 || _buffer.YBase > 0)
            {
                int prevLineIndex = _buffer.Y + _buffer.YBase - 1;
                if (prevLineIndex >= 0)
                {
                    BufferLine? prevLine = _buffer.Lines[prevLineIndex];
                    if (prevLine is { IsWrapped: true })
                    {
                        line = prevLine;
                        prevX = terminal.Cols - 1;
                    }
                    else
                    {
                        return false; // Can't combine at start of unwrapped line
                    }
                }
                else
                {
                    return false; // No previous line
                }
            }
            else
            {
                return false; // At the very beginning of the buffer
            }
        }

        // Get the previous cell
        if (prevX < 0 || prevX >= line.Length)
        {
            return false;
        }

        BufferCell prevCell = line[prevX];

        // Skip placeholder cells (width 0) for wide characters - find the actual character cell
        while (prevX > 0 && prevCell.Width == 0)
        {
            prevX--;
            prevCell = line[prevX];
        }

        // Can't combine with empty cells
        if (prevCell.IsEmpty())
        {
            // Only allow combining with actual content, not empty/space cells
            // unless the space is the only content (which shouldn't happen for valid sequences)
            return false;
        }

        // Append the combining character to the previous cell's content
        string newContent = prevCell.Content + data;

        // Determine if we need to adjust the width

        int newWidth = codePoint switch
        {
            // Handle variation selectors that change presentation
            VARIATION_SELECTOR_EMOJI_SYMBOL when prevCell.Width == 1 => 2,
            VARIATION_SELECTOR_TEXT_SYMBOL when prevCell.Width == 2 => 1,
            _ => prevCell.Width
        };

        // Create the updated cell
        BufferCell updatedCell = new()
        {
            Content = newContent,
            Width = newWidth,
            Attributes = prevCell.Attributes,
            CodePoint = prevCell.CodePoint // Keep the original base code point
        };

        line.SetCell(prevX, ref updatedCell);

        // Handle width changes
        if (newWidth == prevCell.Width)
        {
            return true;
        }

        switch (newWidth)
        {
            case 2 when prevCell.Width == 1:
            {
                // Need to add a spacer cell after the character
                // Check if cursor position needs adjustment
                if (prevX + 1 < terminal.Cols)
                {
                    // Use BufferCell.Spacer with the previous cell's attributes
                    BufferCell spacer = BufferCell.Empty;
                    spacer.Attributes = prevCell.Attributes;
                    line.SetCell(prevX + 1, ref spacer);

                    // Adjust cursor if we're after this cell
                    if (_buffer.X > prevX)
                    {
                        _buffer.SetCursorRaw(Math.Min(_buffer.X + 1, terminal.Cols), _buffer.Y);
                    }
                }

                break;
            }
            case 1 when prevCell.Width == 2:
            {
                // Remove the spacer cell by replacing with whitespace
                if (prevX + 1 < terminal.Cols)
                {
                    // Use BufferCell.Whitespace with the previous cell's attributes
                    BufferCell emptyCell = BufferCell.Space;
                    emptyCell.Attributes = prevCell.Attributes;
                    line.SetCell(prevX + 1, ref emptyCell);

                    // Adjust cursor if we're after this cell
                    if (_buffer.X > prevX + 1)
                    {
                        _buffer.SetCursorRaw(Math.Max(_buffer.X - 1, 0), _buffer.Y);
                    }
                }

                break;
            }
        }

        return true;
    }

    /// <summary>
    ///     Handles CSI sequences (Control Sequence Introducer).
    /// </summary>
    public void HandleCsi(string identifier, Params parameters)
    {
        bool isPrivate = identifier.IsPrivateMode();
        CsiCommand command = identifier.ToCsiCommand();

        switch (command)
        {
            case CsiCommand.InsertChars:
                InsertChars(parameters);
                break;

            case CsiCommand.CursorUp:
                CursorUp(parameters);
                break;

            case CsiCommand.CursorDown:
                CursorDown(parameters);
                break;

            case CsiCommand.CursorForward:
                CursorForward(parameters);
                break;

            case CsiCommand.CursorBackward:
                CursorBackward(parameters);
                break;

            case CsiCommand.CursorNextLine:
                CursorNextLine(parameters);
                break;

            case CsiCommand.CursorPreviousLine:
                CursorPrecedingLine(parameters);
                break;

            case CsiCommand.CursorCharAbsolute:
                CursorCharAbsolute(parameters);
                break;

            case CsiCommand.CursorPosition:
                CursorPosition(parameters);
                break;

            case CsiCommand.CursorForwardTab:
                CursorForwardTab(parameters);
                break;

            case CsiCommand.EraseInDisplay:
                EraseInDisplay(parameters);
                break;

            case CsiCommand.EraseInLine:
                EraseInLine(parameters);
                break;

            case CsiCommand.InsertLines:
                InsertLines(parameters);
                break;

            case CsiCommand.DeleteLines:
                DeleteLines(parameters);
                break;

            case CsiCommand.DeleteChars:
                DeleteChars(parameters);
                break;

            case CsiCommand.ScrollUp:
                ScrollUp(parameters);
                break;

            case CsiCommand.ScrollDown:
                ScrollDown(parameters);
                break;

            case CsiCommand.EraseChars:
                EraseChars(parameters);
                break;

            case CsiCommand.CursorBackwardTab:
                CursorBackwardTab(parameters);
                break;

            case CsiCommand.TabClear:
                TabClear(parameters);
                break;

            case CsiCommand.DeviceAttributes:
                DeviceAttributes(parameters, isPrivate);
                break;

            case CsiCommand.LinePositionAbsolute:
                LinePositionAbsolute(parameters);
                break;

            case CsiCommand.SelectGraphicRendition:
                CharAttributes(parameters);
                break;

            case CsiCommand.DeviceStatusReport:
                DeviceStatusReport(parameters, isPrivate);
                break;

            case CsiCommand.SetScrollRegion:
                SetScrollRegion(parameters);
                break;

            case CsiCommand.SaveCursorAnsi:
                SaveCursorAnsi();
                break;

            case CsiCommand.RestoreCursorAnsi:
                RestoreCursorAnsi();
                break;

            case CsiCommand.WindowManipulation:
                WindowManipulation(parameters);
                break;

            case CsiCommand.SelectCursorStyle:
                SelectCursorStyle(parameters);
                break;

            case CsiCommand.SetMode:
                SetCsiModeParameters(parameters, isPrivate);
                break;

            case CsiCommand.ResetMode:
                // DEC Private Mode Reset (CSI ? Pm l)
                ResetCsiModeParameters(parameters, isPrivate);
                break;

            case CsiCommand.Unknown:
                // Log unknown sequence for debugging
                Debug.WriteLine($"Unknown CSI sequence: {identifier}");
                break;
        }
    }

    /// <summary>
    ///     Handles ESC sequences.
    /// </summary>
    public void HandleEsc(string finalChar, string collected)
    {
        switch (finalChar)
        {
            case "D": // IND - Index
                IndexDown();
                break;
            case "E": // NEL - Next Line
                NextLine();
                break;
            case "M": // RI - Reverse Index
                ReverseIndex();
                break;
            case "c": // RIS - Reset to Initial State
                ResetTerminal();
                break;
            case "7": // DECSC - Save Cursor
                SaveCursor();
                break;
            case "8": // DECRC - Restore Cursor
                RestoreCursor();
                break;
        }

        // Charset designation sequences
        if (collected.Length <= 0)
        {
            return;
        }

        char intermediateChar = collected[0];
        switch (intermediateChar)
        {
            case '(': // Designate G0 character set
                SetCharset(CharsetMode.G0, finalChar);
                break;
            case ')': // Designate G1 character set
                SetCharset(CharsetMode.G1, finalChar);
                break;
            case '*': // Designate G2 character set
                SetCharset(CharsetMode.G2, finalChar);
                break;
            case '+': // Designate G3 character set
                SetCharset(CharsetMode.G3, finalChar);
                break;
            case '#': // DEC line attribute sequences
                HandleDecLineAttribute(finalChar);
                break;
        }
    }

    private void HandleDecLineAttribute(string finalChar)
    {
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return;
        }

        switch (finalChar)
        {
            case "3": line.LineAttribute = LineAttribute.DoubleHeightTop; break;
            case "4": line.LineAttribute = LineAttribute.DoubleHeightBottom; break;
            case "5": line.LineAttribute = LineAttribute.Normal; break;
            case "6": line.LineAttribute = LineAttribute.DoubleWidth; break;
            case "8": FillScreenWithE(); break;
        }
    }

    private void FillScreenWithE()
    {
        BufferCell cell = new("E", 1, AttributeData.Default);
        for (int row = 0; row < terminal.Rows; row++)
        {
            BufferLine? line = _buffer.Lines[_buffer.YBase + row];
            if (line == null)
            {
                continue;
            }

            line.LineAttribute = LineAttribute.Normal;
            line.Fill(cell);
        }

        _buffer.SetCursor(0, 0);
    }

    private void SetCharset(CharsetMode mode, string charsetId)
    {
        Dictionary<char, string>? charset = Charsets.GetCharset(charsetId);
        _charsets[mode] = charset;
    }

    /// <summary>
    ///     Shift Out - Select G1 character set (SO, 0x0E).
    /// </summary>
    public void ShiftOut()
    {
        _currentCharset = CharsetMode.G1;
    }

    /// <summary>
    ///     Shift In - Select G0 character set (SI, 0x0F).
    /// </summary>
    public void ShiftIn()
    {
        _currentCharset = CharsetMode.G0;
    }

    /// <summary>
    ///     Resets charset state to defaults.
    /// </summary>
    public void ResetCharsets()
    {
        _charsets[CharsetMode.G0] = Charsets.Ascii;
        _charsets[CharsetMode.G1] = Charsets.Ascii;
        _charsets[CharsetMode.G2] = Charsets.Ascii;
        _charsets[CharsetMode.G3] = Charsets.Ascii;
        _currentCharset = CharsetMode.G0;
    }

    /// <summary>
    ///     Handles OSC sequences (Operating System Command).
    /// </summary>
    public void HandleOsc(string data)
    {
        string[] parts = data.Split([';'], 2);
        if (parts.Length == 0)
        {
            return;
        }

        string arg = parts.Length > 1 ? parts[1] : string.Empty;

        // Try to parse as OscCommand enum
        if (parts[0].TryParseOscCommand(out OscCommand command))
        {
            switch (command)
            {
                case OscCommand.SetIconAndTitle:
                case OscCommand.SetWindowTitle:
                    terminal.Title = arg;
                    terminal.RaiseTitleChanged(arg);
                    break;

                case OscCommand.SetIconName:
                    // Icon name - not typically supported in modern terminals
                    break;

                case OscCommand.ChangeColor:
                    HandleColorPaletteChange(arg);
                    break;

                case OscCommand.CurrentDirectory:
                    HandleCurrentDirectory(arg);
                    break;

                case OscCommand.Hyperlink:
                    HandleHyperlink(arg);
                    break;

                case OscCommand.ForegroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.BackgroundColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.CursorColor:
                    HandleColorQuery(((int)command).ToString(), arg);
                    break;

                case OscCommand.Clipboard:
                    HandleClipboard(arg);
                    break;

                case OscCommand.ResetColor:
                case OscCommand.ResetForeground:
                case OscCommand.ResetBackground:
                case OscCommand.ResetCursor:
                    HandleColorReset(arg);
                    break;

                default:
                    // Known but unhandled command
                    Debug.WriteLine($"Unhandled OSC command: {command}");
                    break;
            }
        }
        else
        {
            // Unknown or unsupported OSC sequence
            Debug.WriteLine($"Unknown OSC sequence: {parts[0]}");
        }
    }

    private void HandleColorPaletteChange(string data)
    {
        // OSC 4 ; index ; colorspec ST
        // Example: OSC 4;1;rgb:ff/00/00 ST (set color 1 to red)
        // For now, we just acknowledge but don't actually change colors
        // A full implementation would parse the color and store it
        string[] parts = data.Split(';');
        if (parts.Length >= 2)
        {
            // Color index in parts[0], color spec in parts[1]
            // TODO: Implement actual color storage and management
        }
    }

    private void HandleCurrentDirectory(string data)
    {
        // OSC 7 ; file://hostname/path ST
        // Example: OSC 7;file://localhost/home/user ST
        if (data.StartsWith("file://"))
        {
            // Extract path from file:// URL
            string uri = data.Substring(7); // Remove "file://"
            int slashIndex = uri.IndexOf('/');
            if (slashIndex >= 0)
            {
                string path = uri.Substring(slashIndex);
                terminal.CurrentDirectory = Uri.UnescapeDataString(path);
                terminal.RaiseDirectoryChanged(terminal.CurrentDirectory);
            }
        }
    }

    private void HandleHyperlink(string data)
    {
        // OSC 8 ; params ; URI ST
        // Example: OSC 8;;http://example.com ST (start link)
        //          OSC 8;; ST (end link)
        string[] parts = data.Split([';'], 2);

        if (parts.Length >= 2)
        {
            string @params = parts[0];
            string uri = parts[1];

            if (string.IsNullOrEmpty(uri))
            {
                // End hyperlink
                terminal.CurrentHyperlink = null;
                terminal.HyperlinkId = null;
                terminal.RaiseHyperlinkChanged(null);
            }
            else
            {
                // Start hyperlink
                terminal.CurrentHyperlink = uri;

                // Parse params for id= parameter
                if (!string.IsNullOrEmpty(@params))
                {
                    string[] paramParts = @params.Split(':');
                    foreach (string p in paramParts)
                    {
                        if (p.StartsWith("id="))
                        {
                            terminal.HyperlinkId = p.Substring(3);
                        }
                    }
                }

                terminal.RaiseHyperlinkChanged(uri);
            }
        }
    }

    private void HandleColorQuery(string colorType, string data)
    {
        // OSC 10/11/12 with ? queries the color
        // OSC 10/11/12 with color spec sets the color
        if (data == "?")
        {
            // Query color - respond with current color
            // Format: OSC colorType ; rgb:rr/gg/bb ST
            // For now, return a default response
            string response = colorType switch
            {
                "10" => $"\e]{colorType};rgb:ff/ff/ff\a", // Foreground
                "11" => $"\e]{colorType};rgb:00/00/00\a", // Background
                "12" => $"\e]{colorType};rgb:ff/ff/ff\a", // Cursor
                _ => string.Empty
            };

            if (!string.IsNullOrEmpty(response))
            {
                terminal.RaiseDataReceived(response);
            }
        }
        else if (!string.IsNullOrEmpty(data))
        {
            // Set color - would parse and apply the color
            // TODO: Implement actual color setting
        }
    }

    private void HandleClipboard(string data)
    {
        // OSC 52 ; c ; data ST
        // Example: OSC 52;c;base64data ST
        string[] parts = data.Split([';'], 2);

        if (parts.Length < 2)
        {
            return;
        }

        string target = parts[0]; // Usually 'c' for clipboard, 'p' for primary
        string clipdata = parts[1];

        if (clipdata == "?")
        {
            // Query clipboard - respond with clipboard content
            // Format: OSC 52 ; c ; base64data ST
            // For security, many terminals don't support this
            // We'll send an empty response
            terminal.RaiseDataReceived($"\e]52;{target};\a");
        }
        else
        {
            // Set clipboard
            try
            {
                byte[] decoded = Convert.FromBase64String(clipdata);
                string text = Encoding.UTF8.GetString(decoded);
                // TODO: Integrate with system clipboard
                // For now, we just acknowledge receipt
            }
            catch
            {
                // Invalid base64 or encoding
            }
        }
    }

    private void HandleColorReset(string data)
    {
        // OSC 104 ; index ST (reset specific color)
        // OSC 104 ST (reset all colors)
        // TODO: Implement color reset functionality
    }

    // CSI Handler Implementations

    private void CursorUp(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorDown(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(_buffer.X, Math.Min(_buffer.Y + count, terminal.Rows - 1));
    }

    private void CursorForward(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Min(_buffer.X + count, terminal.Cols - 1), _buffer.Y);
    }

    private void CursorBackward(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(Math.Max(_buffer.X - count, 0), _buffer.Y);
    }

    private void CursorNextLine(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Min(_buffer.Y + count, terminal.Rows - 1));
    }

    private void CursorPrecedingLine(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.SetCursor(0, Math.Max(_buffer.Y - count, 0));
    }

    private void CursorCharAbsolute(Params parameters)
    {
        int col = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        _buffer.SetCursor(col, _buffer.Y);
    }

    private void CursorPosition(Params parameters)
    {
        int row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        int col = Math.Max(parameters.GetParam(1, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        _buffer.SetCursor(col, row);
    }

    private void EraseInDisplay(Params parameters)
    {
        int mode = parameters.GetParam(0);
        BufferCell emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        switch (mode)
        {
            case 0: // Erase below
                EraseInLine(parameters); // Current line from cursor
                for (int i = _buffer.Y + 1; i < terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }

                break;
            case 1: // Erase above
                for (int i = 0; i < _buffer.Y; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }

                EraseInLine(parameters); // Current line to cursor
                break;
            case 2: // Erase all
                for (int i = 0; i < terminal.Rows; i++)
                {
                    _buffer.Lines[_buffer.YBase + i]?.Fill(emptyCell);
                }

                break;
            case 3: // Erase scrollback
                _buffer.EraseScrollback();
                break;
        }
    }

    private void EraseInLine(Params parameters)
    {
        int mode = parameters.GetParam(0);
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return;
        }

        BufferCell emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        switch (mode)
        {
            case 0: // Erase to right
                line.Fill(emptyCell, _buffer.X, terminal.Cols);
                break;
            case 1: // Erase to left
                line.Fill(emptyCell, 0, _buffer.X + 1);
                break;
            case 2: // Erase entire line
                line.Fill(emptyCell);
                break;
        }
    }

    private void InsertLines(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        // Only works in scroll region
        if (_buffer.Y < _buffer.ScrollTop || _buffer.Y > _buffer.ScrollBottom)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 1);
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 0,
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void DeleteLines(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        // Only works in scroll region
        if (_buffer.Y < _buffer.ScrollTop || _buffer.Y > _buffer.ScrollBottom)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            _buffer.Lines.Splice(_buffer.Y + _buffer.YBase, 1);
            _buffer.Lines.Splice(_buffer.YBase + _buffer.ScrollBottom, 0,
                _buffer.GetBlankLine(_curAttr));
        }
    }

    private void InsertChars(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return;
        }

        // Shift cells right from cursor position
        line.CopyCellsFrom(line, _buffer.X, _buffer.X + count,
            terminal.Cols - _buffer.X - count, false);

        // Blank the inserted cells at cursor position
        BufferCell emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;
        line.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, terminal.Cols));
    }

    private void DeleteChars(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];
        if (line == null)
        {
            return;
        }

        // Limit count to remaining characters on line
        int remaining = terminal.Cols - _buffer.X;
        count = Math.Min(count, remaining);

        line.CopyCellsFrom(line, _buffer.X + count, _buffer.X,
            terminal.Cols - _buffer.X - count, false);

        // Fill vacated cells at right edge with current attributes (BCE)
        BufferCell emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;
        line.Fill(emptyCell, terminal.Cols - count, terminal.Cols);
    }

    private void EraseChars(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        BufferLine? line = _buffer.Lines[_buffer.Y + _buffer.YBase];

        BufferCell emptyCell = BufferCell.Space;
        emptyCell.Attributes = _curAttr;

        line?.Fill(emptyCell, _buffer.X, Math.Min(_buffer.X + count, terminal.Cols));
    }

    private void ScrollUp(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollUp(count);
    }

    private void ScrollDown(Params parameters)
    {
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        _buffer.ScrollDown(count);
    }

    private void SaveCursorAnsi()
    {
        // ANSI save cursor (CSI s) - same as DEC DECSC but simpler
        SaveCursor();
    }

    private void RestoreCursorAnsi()
    {
        // ANSI restore cursor (CSI u) - same as DEC DECRC but simpler
        RestoreCursor();
    }

    private void LinePositionAbsolute(Params parameters)
    {
        // VPA - Line Position Absolute (CSI d)
        int row = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        row = GetAbsoluteCursorRow(row);
        _buffer.SetCursor(_buffer.X, row);
    }

    private void CursorForwardTab(Params parameters)
    {
        // CHT - Cursor Forward Tabulation (CSI I)
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        int tabWidth = terminal.Options.TabStopWidth;

        for (int i = 0; i < count; i++)
        {
            int nextTabStop = ((_buffer.X / tabWidth) + 1) * tabWidth;
            _buffer.SetCursor(Math.Min(nextTabStop, terminal.Cols - 1), _buffer.Y);
        }
    }

    private void CursorBackwardTab(Params parameters)
    {
        // CBT - Cursor Backward Tabulation (CSI Z)
        int count = Math.Max(parameters.GetParam(0, 1), 1);
        int tabWidth = terminal.Options.TabStopWidth;

        for (int i = 0; i < count; i++)
        {
            if (_buffer.X == 0)
            {
                break;
            }

            int prevTabStop = (_buffer.X - 1) / tabWidth * tabWidth;
            _buffer.SetCursor(Math.Max(prevTabStop, 0), _buffer.Y);
        }
    }

    private void TabClear(Params parameters)
    {
        // TBC - Tab Clear (CSI g)
        // Ps = 0: Clear tab stop at current column (default)
        // Ps = 3: Clear all tab stops
        // Note: We use fixed tab stops, so this is acknowledged but has no effect
        // A full implementation would maintain a list of custom tab stops
        int mode = parameters.GetParam(0);
        switch (mode)
        {
            case 0:
                // Clear current column tab stop - acknowledged but no action
                break;
            case 3:
                // Clear all tab stops - acknowledged but no action
                break;
        }
    }

    private void DeviceAttributes(Params parameters, bool isPrivate)
    {
        // DA - Device Attributes (CSI c or CSI > c)
        if (isPrivate)
        {
            // Secondary DA (CSI > c) - Report terminal ID and version
            // Response: CSI > 0 ; version ; 0 c
            // We report as VT100-compatible
            terminal.RaiseDataReceived("\e[>0;10;0c");
        }
        else
        {
            // Primary DA (CSI c) - Report device attributes
            // Response: CSI ? 1 ; 2 c (VT100 with AVO)
            // More complete: CSI ? 1 ; 2 ; 6 ; 9 c
            // 1 = 132 columns, 2 = Printer, 6 = Selective erase, 9 = National replacement character sets
            terminal.RaiseDataReceived("\e[?1;2c");
        }
    }

    private void DeviceStatusReport(Params parameters, bool isPrivate)
    {
        // DSR - Device Status Report (CSI n or CSI ? n)
        int report = parameters.GetParam(0);

        if (isPrivate)
        {
            // DEC-specific DSR
            switch (report)
            {
                case 6: // DECXCPR - Extended Cursor Position Report
                    // Report cursor position: CSI ? row ; col R
                    int row = _buffer.Y + 1; // 1-based
                    int col = _buffer.X + 1; // 1-based
                    terminal.RaiseDataReceived($"\e[?{row};{col}R");
                    break;

                case 15: // Printer status
                    // Report no printer: CSI ? 1 3 n
                    terminal.RaiseDataReceived("\e[?13n");
                    break;

                case 25: // UDK status
                    // Report UDK locked: CSI ? 2 1 n
                    terminal.RaiseDataReceived("\e[?21n");
                    break;

                case 26: // Keyboard status
                    // Report keyboard ready: CSI ? 2 7 ; 1 ; 0 ; 0 n
                    terminal.RaiseDataReceived("\e[?27;1;0;0n");
                    break;
            }
        }
        else
        {
            // Standard ANSI DSR
            switch (report)
            {
                case 5: // Operating status
                    // Report OK: CSI 0 n
                    terminal.RaiseDataReceived("\e[0n");
                    break;

                case 6: // CPR - Cursor Position Report
                    // Report cursor position: CSI row ; col R
                    int row = _buffer.Y + 1; // 1-based
                    int col = _buffer.X + 1; // 1-based

                    // Adjust for origin mode
                    if (terminal.OriginMode)
                    {
                        row = row - _buffer.ScrollTop;
                    }

                    terminal.RaiseDataReceived($"\e[{row};{col}R");
                    break;
            }
        }
    }

    private void CharAttributes(Params parameters)
    {
        if (parameters.Length == 0)
        {
            _curAttr = AttributeData.Default;
            return;
        }

        for (int i = 0; i < parameters.Length; i++)
        {
            int param = parameters.GetParam(i);

            switch (param)
            {
                case 0: // Reset
                    _curAttr = AttributeData.Default;
                    break;
                case 1: // Bold
                    _curAttr.SetBold(true);
                    break;
                case 2: // Dim
                    _curAttr.SetDim(true);
                    break;
                case 3: // Italic
                    _curAttr.SetItalic(true);
                    break;
                case 4: // Underline
                    _curAttr.SetUnderline(true);
                    break;
                case 5: // Blink
                    _curAttr.SetBlink(true);
                    break;
                case 7: // Inverse
                    _curAttr.SetInverse(true);
                    break;
                case 8: // Invisible
                    _curAttr.SetInvisible(true);
                    break;
                case 9: // Strikethrough
                    _curAttr.SetStrikethrough(true);
                    break;
                case 22: // Not bold/dim
                    _curAttr.SetBold(false);
                    _curAttr.SetDim(false);
                    break;
                case 23: // Not italic
                    _curAttr.SetItalic(false);
                    break;
                case 24: // Not underline
                    _curAttr.SetUnderline(false);
                    break;
                case 25: // Not blink
                    _curAttr.SetBlink(false);
                    break;
                case 27: // Not inverse
                    _curAttr.SetInverse(false);
                    break;
                case 28: // Not invisible
                    _curAttr.SetInvisible(false);
                    break;
                case 29: // Not strikethrough
                    _curAttr.SetStrikethrough(false);
                    break;
                case >= 30 and <= 37: // Foreground color
                    _curAttr.SetFgColor(param - 30);
                    break;
                case 38: // Extended foreground color
                    i = HandleExtendedColor(parameters, i, true);
                    break;
                case 39: // Default foreground
                    _curAttr.SetFgColor(256);
                    break;
                case >= 40 and <= 47: // Background color
                    _curAttr.SetBgColor(param - 40);
                    break;
                case 48: // Extended background color
                    i = HandleExtendedColor(parameters, i, false);
                    break;
                case 49: // Default background
                    _curAttr.SetBgColor(257);
                    break;
                case >= 90 and <= 97: // Bright foreground color
                    _curAttr.SetFgColor(param - 90 + 8);
                    break;
                case >= 100 and <= 107: // Bright background color
                    _curAttr.SetBgColor(param - 100 + 8);
                    break;
            }
        }
    }

    private int HandleExtendedColor(Params parameters, int index, bool isForeground)
    {
        if (index + 1 >= parameters.Length)
        {
            return index;
        }

        int colorType = parameters.GetParam(index + 1);

        if (colorType == 2 && index + 4 < parameters.Length) // RGB
        {
            int r = parameters.GetParam(index + 2);
            int g = parameters.GetParam(index + 3);
            int b = parameters.GetParam(index + 4);
            int rgb = (r << 16) | (g << 8) | b;

            if (isForeground)
            {
                _curAttr.SetFgColor(rgb, 1);
            }
            else
            {
                _curAttr.SetBgColor(rgb, 1);
            }

            return index + 4;
        }

        if (colorType == 5 && index + 2 < parameters.Length) // 256 color
        {
            int color = parameters.GetParam(index + 2);

            if (isForeground)
            {
                _curAttr.SetFgColor(color);
            }
            else
            {
                _curAttr.SetBgColor(color);
            }

            return index + 2;
        }

        return index;
    }

    private void SetScrollRegion(Params parameters)
    {
        int top = Math.Max(parameters.GetParam(0, 1), 1) - 1;
        int bottom = Math.Max(parameters.GetParam(1, terminal.Rows), 1) - 1;
        _buffer.SetScrollRegion(top, bottom);
        MoveCursorToHome();
    }

    private int GetAbsoluteCursorRow(int row)
    {
        if (terminal.OriginMode)
        {
            long absoluteRow = (long)_buffer.ScrollTop + row;
            return (int)Math.Clamp(absoluteRow, _buffer.ScrollTop, _buffer.ScrollBottom);
        }

        return Math.Clamp(row, 0, terminal.Rows - 1);
    }

    private void MoveCursorToHome()
    {
        int row = terminal.OriginMode ? _buffer.ScrollTop : 0;
        _buffer.SetCursor(0, row);
    }

    private void WindowManipulation(Params parameters)
    {
        // CSI Ps ; Ps ; Ps t - Window manipulation (XTWINOPS)
        // Check WindowOptions permissions before firing events
        int operation = parameters.GetParam(0);

        switch (operation)
        {
            case 1: // De-iconify window (restore from minimized)
                if (terminal.Options.WindowOptions.RestoreWin)
                {
                    terminal.RaiseWindowRestored();
                }

                break;

            case 2: // Iconify window (minimize)
                if (terminal.Options.WindowOptions.MinimizeWin)
                {
                    terminal.RaiseWindowMinimized();
                }

                break;

            case 3: // Move window to x, y
                if (terminal.Options.WindowOptions.SetWinPosition)
                {
                    int x = parameters.GetParam(1);
                    int y = parameters.GetParam(2);
                    terminal.RaiseWindowMoved(x, y);
                }

                break;

            case 4: // Resize window to height, width pixels
                if (terminal.Options.WindowOptions.SetWinSizePixels)
                {
                    int height = parameters.GetParam(1);
                    int width = parameters.GetParam(2);
                    terminal.RaiseWindowResized(width, height);
                }

                break;

            case 5: // Raise window to front
                if (terminal.Options.WindowOptions.RaiseWin)
                {
                    terminal.RaiseWindowRaised();
                }

                break;

            case 6: // Lower window to back
                if (terminal.Options.WindowOptions.LowerWin)
                {
                    terminal.RaiseWindowLowered();
                }

                break;

            case 7: // Refresh window
                if (terminal.Options.WindowOptions.RefreshWin)
                {
                    terminal.RaiseWindowRefreshed();
                }

                break;

            case 8: // Resize text area to height, width characters
                if (terminal.Options.WindowOptions.SetWinSizeChars)
                {
                    int rows = parameters.GetParam(1);
                    int cols = parameters.GetParam(2);
                    if (rows > 0 && cols > 0)
                    {
                        terminal.Resize(cols, rows);
                    }
                }

                break;

            case 9: // Maximize/restore operations
                int subOp = parameters.GetParam(1);
                if (subOp == 0 && terminal.Options.WindowOptions.RestoreWin)
                {
                    // Restore maximized window
                    terminal.RaiseWindowRestored();
                }
                else if (subOp == 1 && terminal.Options.WindowOptions.MaximizeWin)
                {
                    // Maximize window
                    terminal.RaiseWindowMaximized();
                }

                break;

            case 10: // Full-screen operations
                subOp = parameters.GetParam(1);
                if (subOp == 0 && terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Exit full-screen
                    terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 1 && terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Enter full-screen
                    terminal.RaiseWindowFullscreened();
                }
                else if (subOp == 2 && terminal.Options.WindowOptions.FullscreenWin)
                {
                    // Toggle full-screen
                    terminal.RaiseWindowFullscreened();
                }

                break;

            case 11: // Report window state (iconified or not)
                if (terminal.Options.WindowOptions.GetWinState)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.State);
                    if (args.Handled)
                    {
                        // Response: CSI 1 t (not iconified) or CSI 2 t (iconified)
                        int stateCode = args.IsIconified ? 2 : 1;
                        terminal.RaiseDataReceived($"\e[{stateCode}t");
                    }
                }

                break;

            case 13: // Report window position
                if (terminal.Options.WindowOptions.GetWinPosition)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.Position);
                    if (args.Handled)
                    {
                        // Response: CSI 3 ; x ; y t
                        terminal.RaiseDataReceived($"\e[3;{args.X};{args.Y}t");
                    }
                }

                break;

            case 14: // Report window size in pixels
                if (terminal.Options.WindowOptions.GetWinSizePixels)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.SizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 4 ; height ; width t
                        terminal.RaiseDataReceived($"\e[4;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }

                break;

            case 15: // Report screen size in pixels
                if (terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.ScreenSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 5 ; height ; width t
                        terminal.RaiseDataReceived($"\e[5;{args.HeightPixels};{args.WidthPixels}t");
                    }
                }

                break;

            case 16: // Report character cell size in pixels
                if (terminal.Options.WindowOptions.GetCellSizePixels)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.CellSizePixels);
                    if (args.Handled)
                    {
                        // Response: CSI 6 ; height ; width t
                        terminal.RaiseDataReceived($"\e[6;{args.CellHeight};{args.CellWidth}t");
                    }
                }

                break;

            case 18: // Report text area size in characters
                if (terminal.Options.WindowOptions.GetWinSizeChars)
                {
                    // Response: CSI 8 ; rows ; cols t
                    terminal.RaiseDataReceived($"\e[8;{terminal.Rows};{terminal.Cols}t");
                }

                break;

            case 19: // Report screen size in characters
                if (terminal.Options.WindowOptions.GetScreenSizePixels)
                {
                    // This is typically the same as window size for terminal apps
                    terminal.RaiseDataReceived($"\e[9;{terminal.Rows};{terminal.Cols}t");
                }

                break;

            case 20: // Report icon label
                if (terminal.Options.WindowOptions.GetIconTitle)
                {
                    TerminalEvents.WindowInfoRequestedEventArgs args =
                        terminal.RaiseWindowInfoRequested(WindowInfoRequest.IconTitle);
                    if (args is { Handled: true, Title: not null })
                    {
                        // Response: OSC L label ST
                        terminal.RaiseDataReceived($"\e]L{args.Title}\a");
                    }
                }

                break;

            case 21: // Report window title
                if (terminal.Options.WindowOptions.GetWinTitle)
                {
                    // Response: OSC l title ST - use the terminal's current title
                    string title = terminal.Title;
                    terminal.RaiseDataReceived($"\e]l{title}\a");
                }

                break;

            case 22: // Save window title
                // Push title onto stack (not typically implemented)
                break;

            case 23: // Restore window title
                // Pop title from stack (not typically implemented)
                break;
        }
    }

    private void SetCsiModeParameters(Params parameters, bool isPrivate)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            int mode = parameters.GetParam(i);
            SetCsiMode(mode, isPrivate);
        }
    }

    private void SetCsiMode(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECSET)
            // Convert int to TerminalMode enum
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                Debug.WriteLine($"Unknown CSI private terminal mode: {mode}");
                return;
            }

            TerminalMode terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.AppCursorKeys:
                    terminal.ApplicationCursorKeys = true;
                    break;

                case TerminalMode.InsertMode:
                    // Mode 4: In DEC private mode context, this is SmoothScroll (DECSCLM)
                    // InsertMode and SmoothScroll share value 4 in the enum
                    // Smooth scroll is acknowledged but has no effect in modern terminals
                    break;

                case TerminalMode.ReverseVideo:
                    terminal.ReverseVideo = true;
                    break;

                case TerminalMode.Origin:
                    terminal.OriginMode = true;
                    MoveCursorToHome();
                    break;

                case TerminalMode.Wraparound:
                    // Mode 7: Wraparound mode
                    // Wraparound and AutoWrapMode share value 7 in the enum
                    terminal.Options.Wraparound = true;
                    break;

                case TerminalMode.AutoRepeat:
                    // Auto repeat is typically always enabled in modern terminals
                    // This mode is acknowledged but has no effect
                    break;

                case TerminalMode.ShowCursor:
                    terminal.CursorVisible = true;
                    break;

                case TerminalMode.NationalCharset:
                    // National replacement character set mode
                    // Acknowledged but typically no specific action needed for modern use
                    break;

                case TerminalMode.ReverseWraparound:
                    terminal.ReverseWraparound = true;
                    break;

                case TerminalMode.AppKeypad:
                    terminal.ApplicationKeypad = true;
                    break;

                case TerminalMode.BracketedPasteMode:
                    terminal.BracketedPasteMode = true;
                    break;

                case TerminalMode.AltBuffer:
                    terminal.SwitchToAltBuffer();
                    break;

                case TerminalMode.AltBufferCursor:
                    SaveCursor();
                    terminal.SwitchToAltBuffer();
                    break;

                case TerminalMode.AltBufferFull:
                    SaveCursor();
                    terminal.SwitchToAltBuffer();
                    _buffer.SetCursor(0, 0);
                    EraseInDisplay(new Params()); // Clear screen
                    break;

                case TerminalMode.SendFocusEvents:
                    terminal.SendFocusEvents = true;
                    terminal.GetMouseTracker().FocusEvents = true;
                    break;

                case TerminalMode.MouseReportClick:
                    terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.X10;
                    break;

                case TerminalMode.MouseReportNormal:
                    terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.Vt200;
                    break;

                case TerminalMode.MouseReportButtonEvent:
                    terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.ButtonEvent;
                    break;

                case TerminalMode.MouseReportAnyEvent:
                    terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.AnyEvent;
                    break;

                case TerminalMode.MouseReportUtf8:
                    terminal.GetMouseTracker().Encoding = MouseEncoding.Utf8;
                    break;

                case TerminalMode.MouseReportSgr:
                    terminal.GetMouseTracker().Encoding = MouseEncoding.Sgr;
                    break;

                case TerminalMode.MouseReportUrxvt:
                    terminal.GetMouseTracker().Encoding = MouseEncoding.Urxvt;
                    break;

                case TerminalMode.EightBitInput:
                    terminal.EightBitInput = true;
                    break;

                case TerminalMode.NumLock:
                    // NumLock modifier handling - acknowledge but no specific action needed
                    break;

                case TerminalMode.MetaSendsEscape:
                    Debug.WriteLine($">>> Mode {mode} MetaSendsEscape ENABLED (disabling Win32InputMode)");
                    terminal.MetaSendsEscape = true;
                    // MetaSendsEscape is incompatible with Win32InputMode for Alt key handling
                    // When explicitly requesting ESC+char for meta keys, disable Win32 input
                    terminal.Win32InputMode = false;
                    break;

                case TerminalMode.AltSendsEscape:
                    Debug.WriteLine($">>> Mode {mode} AltSendsEscape ENABLED (disabling Win32InputMode)");
                    terminal.AltSendsEscape = true;
                    // AltSendsEscape is incompatible with Win32InputMode for Alt key handling
                    // When explicitly requesting ESC+char for Alt keys, disable Win32 input
                    terminal.Win32InputMode = false;
                    break;

                case TerminalMode.Win32InputMode:
                    Debug.WriteLine(
                        $">>> Mode {mode} Win32InputMode ENABLED (disabling MetaSendsEscape and AltSendsEscape)");
                    terminal.Win32InputMode = true;
                    // Win32InputMode is incompatible with MetaSendsEscape/AltSendsEscape
                    // When enabling Win32 input mode, disable ESC+char modes
                    terminal.MetaSendsEscape = false;
                    terminal.AltSendsEscape = false;
                    break;

                default:
                    Debug.WriteLine($"Unhandled CSI private terminal mode: {terminalMode}");
                    break;
            }
        }
        else
        {
            // ANSI Modes (SM)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                Debug.WriteLine($"Unknown CSI terminal mode: {mode}");
                return;
            }

            TerminalMode terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.InsertMode:
                    terminal.InsertMode = true;
                    break;

                case TerminalMode.AutoWrapMode:
                    terminal.Options.Wraparound = true;
                    break;

                default:
                    Debug.WriteLine($"Unhandled CSI terminal mode: {terminalMode}");
                    break;
            }
        }
    }

    private void ResetCsiModeParameters(Params parameters, bool isPrivate)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            int mode = parameters.GetParam(i);
            ResetCsiMode(mode, isPrivate);
        }
    }

    private void ResetCsiMode(int mode, bool isPrivate)
    {
        if (isPrivate)
        {
            // DEC Private Modes (DECRST)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                Debug.WriteLine($"Unknown private reset terminal mode: {mode}");
                return;
            }

            TerminalMode terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.AppCursorKeys:
                    terminal.ApplicationCursorKeys = false;
                    break;

                case TerminalMode.InsertMode:
                    // Mode 4: In DEC private mode context, this is SmoothScroll (DECSCLM)
                    // Smooth scroll is acknowledged but has no effect in modern terminals
                    break;

                case TerminalMode.ReverseVideo:
                    terminal.ReverseVideo = false;
                    break;

                case TerminalMode.Origin:
                    terminal.OriginMode = false;
                    MoveCursorToHome();
                    break;

                case TerminalMode.Wraparound:
                    // Mode 7: Wraparound mode
                    terminal.Options.Wraparound = false;
                    break;

                case TerminalMode.AutoRepeat:
                    // Auto repeat is typically always enabled in modern terminals
                    // This mode is acknowledged but has no effect
                    break;

                case TerminalMode.ShowCursor:
                    terminal.CursorVisible = false;
                    break;

                case TerminalMode.NationalCharset:
                    // National replacement character set mode
                    // Acknowledged but typically no specific action needed for modern use
                    break;

                case TerminalMode.ReverseWraparound:
                    terminal.ReverseWraparound = false;
                    break;

                case TerminalMode.AppKeypad:
                    terminal.ApplicationKeypad = false;
                    break;

                case TerminalMode.BracketedPasteMode:
                    terminal.BracketedPasteMode = false;
                    break;

                case TerminalMode.AltBuffer:
                    terminal.SwitchToNormalBuffer();
                    break;

                case TerminalMode.AltBufferCursor:
                    terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;

                case TerminalMode.AltBufferFull:
                    terminal.SwitchToNormalBuffer();
                    RestoreCursor();
                    break;

                case TerminalMode.SendFocusEvents:
                    terminal.SendFocusEvents = false;
                    terminal.GetMouseTracker().FocusEvents = false;
                    break;

                case TerminalMode.MouseReportClick:
                case TerminalMode.MouseReportNormal:
                case TerminalMode.MouseReportButtonEvent:
                case TerminalMode.MouseReportAnyEvent:
                    terminal.GetMouseTracker().TrackingMode = MouseTrackingMode.None;
                    break;

                case TerminalMode.MouseReportUtf8:
                case TerminalMode.MouseReportSgr:
                case TerminalMode.MouseReportUrxvt:
                    terminal.GetMouseTracker().Encoding = MouseEncoding.Default;
                    break;

                case TerminalMode.EightBitInput:
                    terminal.EightBitInput = false;
                    break;

                case TerminalMode.NumLock:
                    // NumLock modifier handling - acknowledge but no specific action needed
                    break;

                case TerminalMode.MetaSendsEscape:
                    Debug.WriteLine($">>> Mode {mode} MetaSendsEscape DISABLED");
                    terminal.MetaSendsEscape = false;
                    break;

                case TerminalMode.AltSendsEscape:
                    Debug.WriteLine($">>> Mode {mode} AltSendsEscape DISABLED");
                    terminal.AltSendsEscape = false;
                    break;

                case TerminalMode.Win32InputMode:
                    Debug.WriteLine($">>> Mode {mode} Win32InputMode DISABLED");
                    terminal.Win32InputMode = false;
                    break;

                default:
                    Debug.WriteLine($"Unhandled terminal mode: {terminalMode}");
                    break;
            }
        }
        else
        {
            // ANSI Modes (RM)
            if (!Enum.IsDefined(typeof(TerminalMode), mode))
            {
                Debug.WriteLine($"Unknown CSI reset terminal mode: {mode}");
                return;
            }

            TerminalMode terminalMode = (TerminalMode)mode;

            switch (terminalMode)
            {
                case TerminalMode.InsertMode:
                    terminal.InsertMode = false;
                    break;

                case TerminalMode.AutoWrapMode:
                    terminal.Options.Wraparound = false;
                    break;

                default:
                    Debug.WriteLine($"Unhandled CSI reset terminal mode: {terminalMode}");
                    break;
            }
        }
    }

    // ESC Handler Implementations

    private void IndexDown()
    {
        if (_buffer.Y == _buffer.ScrollBottom)
        {
            _buffer.ScrollUp(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y + 1);
        }
    }

    private void NextLine()
    {
        IndexDown();
        _buffer.SetCursor(0, _buffer.Y);
    }

    private void ReverseIndex()
    {
        if (_buffer.Y == _buffer.ScrollTop)
        {
            _buffer.ScrollDown(1);
        }
        else
        {
            _buffer.SetCursor(_buffer.X, _buffer.Y - 1);
        }
    }

    private void ResetTerminal()
    {
        terminal.Reset();
    }

    private void SelectCursorStyle(Params parameters)
    {
        // DECSCUSR - Select Cursor Style (CSI Ps SP q)
        int ps = parameters.GetParam(0, 1);

        CursorStyle style;
        bool blink;

        switch (ps)
        {
            case 0:
            case 1:
                style = CursorStyle.Block;
                blink = true;
                break;
            case 2:
                style = CursorStyle.Block;
                blink = false;
                break;
            case 3:
                style = CursorStyle.Underline;
                blink = true;
                break;
            case 4:
                style = CursorStyle.Underline;
                blink = false;
                break;
            case 5:
                style = CursorStyle.Bar;
                blink = true;
                break;
            case 6:
                style = CursorStyle.Bar;
                blink = false;
                break;
            default:
                // Unsupported value - ignore
                return;
        }

        terminal.SetCursorStyle(style, blink);
    }

    private void SaveCursor()
    {
        _buffer.SavedCursorState.X = _buffer.X;
        _buffer.SavedCursorState.Y = _buffer.Y;
        _buffer.SavedCursorState.Attr = _curAttr;
    }

    private void RestoreCursor()
    {
        _buffer.SetCursor(_buffer.SavedCursorState.X, _buffer.SavedCursorState.Y);
        _curAttr = _buffer.SavedCursorState.Attr;
    }

    // Utility Methods

    private int GetStringCellWidth(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        bool supportsComplexEmoji = true;
        ushort width = 0;
        ushort lastWidth = 0;
        int regionalRuneCount = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int runeWidth = UnicodeCalculator.GetWidth(rune);
            if (runeWidth >= 0)
            {
                if (rune.Value == Emoji.ZeroWidthJoiner || rune.Value == Emoji.ObjectReplacementCharacter)
                {
                    if (supportsComplexEmoji)
                    {
                        width -= lastWidth;
                    }
                    else
                        // we return the first emoji as the result because terminal doesn't support chaining them
                    {
                        break;
                    }
                }
                else if (rune.Value == Codepoints.VariationSelectors.EmojiSymbol &&
                         lastWidth == 1)
                {
                    // adjust for the emoji presentation, which is width 2
                    width++;
                    lastWidth = 2;
                }
                else if (rune.Value == Codepoints.VariationSelectors.TextSymbol &&
                         lastWidth == 2)
                {
                    // adjust for the text presentation, which is width 1
                    width--;
                    lastWidth = 1;
                }
                else if (lastWidth > 0 &&
                         ((rune.Value >= Emoji.SkinTones.Light && rune.Value <= Emoji.SkinTones.Dark) ||
                          rune.Value == Codepoints.Keycap))
                {
                    // Emoji modifier (skin tone) or keycap extender should continue current glyph

                    // else: combining � ignore
                }
                // regional indicator symbols
                else if (rune.Value is >= 0x1F1E6 and <= 0x1F1FF)
                {
                    regionalRuneCount++;
                    if (regionalRuneCount % 2 == 0)
                        // every pair of regional indicator symbols form a single glyph
                    {
                        width += (ushort)runeWidth;
                    }
                    // If the last rune is a regional indicator symbol, continue the current glyph
                }
                else
                {
                    width += (ushort)runeWidth;
                }


                if (runeWidth > 0)
                {
                    lastWidth = (ushort)runeWidth;
                }
            }
            // Control chars return as width < 0
            else
            {
                if (rune.Value == 0x9 /* tab */)
                {
                    // Avalonia uses hard coded 4 spaces for tabs (NOT column based tabstops), this may change in the future
                    width += 4;
                    lastWidth = 4;
                }
                else if (rune.Value == '\n')
                {
                    width += 1;
                    lastWidth = 1;
                }
            }
        }

        return width;
    }

    public void SetBuffer(TerminalBuffer buffer)
    {
        _buffer = buffer;
    }
}