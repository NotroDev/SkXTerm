using System.Text;
using XTerm.Common;

namespace XTerm.Buffer;

/// <summary>
///     Main terminal buffer that manages the active screen and scrollback.
/// </summary>
public class TerminalBuffer
{
    public TerminalBuffer(int cols, int rows, int scrollback)
    {
        Cols = cols;
        Rows = rows;
        Lines = new CircularList<BufferLine>(rows + scrollback);
        YDisp = 0;
        BaseY = 0;
        Y = 0;
        X = 0;
        ScrollTop = 0;
        ScrollBottom = rows - 1;
        SavedCursorState = new SavedCursor();

        // Initialize buffer with empty lines
        for (int i = 0; i < rows; i++)
        {
            Lines.Push(new BufferLine(cols, BufferCell.Space));
        }
    }

    /// <summary>
    ///     The absolute line index of the top of the viewport in the buffer.
    ///     In XTerm.js this is 'ydisp'. This represents the current scroll position.
    /// </summary>
    public int ViewportY
    {
        get => YDisp;
        set => YDisp = Math.Clamp(value, 0, BaseY);
    }

    /// <summary>
    ///     The absolute line index where new content is being written.
    ///     In XTerm.js this is 'ybase'. This represents the bottom of the active content.
    /// </summary>
    public int BaseY { get; private set; }

    /// <summary>
    ///     Total number of lines in the buffer (scrollback + active lines).
    /// </summary>
    public int Length => Lines.Length;

    /// <summary>
    ///     Whether the viewport is at the bottom (showing latest content).
    ///     In xterm.js: ydisp === ybase means we're at the bottom.
    /// </summary>
    public bool IsAtBottom => YDisp >= BaseY;

    /// <summary>
    ///     Number of columns in the buffer.
    /// </summary>
    public int Cols { get; private set; }

    /// <summary>
    ///     Number of rows in the buffer (viewport height).
    /// </summary>
    public int Rows { get; private set; }

    // Legacy properties for backward compatibility
    public int YDisp { get; private set; }

    public int YBase => BaseY;
    public int Y { get; private set; }

    public int X { get; private set; }

    public int ScrollTop { get; private set; }

    public int ScrollBottom { get; private set; }

    public CircularList<BufferLine> Lines { get; }

    public SavedCursor SavedCursorState { get; set; }

    /// <summary>
    ///     Fired when lines are trimmed from the start of the buffer.
    /// </summary>
    public event Action<int>? Trimmed;

    /// <summary>
    ///     Gets a line from the buffer.
    /// </summary>
    public BufferLine? GetLine(int y)
    {
        return Lines[y];
    }

    /// <summary>
    ///     Gets a blank line (filled with null cells).
    /// </summary>
    public BufferLine GetBlankLine(AttributeData attr, bool isWrapped = false)
    {
        BufferCell fillCell = BufferCell.Space;
        fillCell.Attributes = attr;
        return new BufferLine(Cols, fillCell) { IsWrapped = isWrapped };
    }

    /// <summary>
    ///     Scrolls the buffer up by a specified number of lines.
    ///     This matches xterm.js Buffer.scroll() behavior.
    /// </summary>
    public void ScrollUp(int lines, bool isWrapped = false)
    {
        for (int i = 0; i < lines; i++)
        {
            // Create a new blank line that will be inserted at the bottom of the scroll region
            BufferLine newLine = GetBlankLine(AttributeData.Default, isWrapped);

            // Only the full-screen scroll region contributes to scrollback.
            // Top-anchored partial regions reserve rows below the margin and
            // must scroll in place so prompts/status rows are not promoted.
            if (ScrollTop == 0 && ScrollBottom == Rows - 1 && Lines.MaxLength > Rows)
            {
                // When scrollTop is 0, the top line goes into scrollback.
                // In xterm.js: push new line first, then increment yBase and yDisp.
                // This causes the circular list to potentially recycle the oldest line.

                // Check if we're at max capacity - if so, yBase stays the same but 
                // the buffer rotates. If not, yBase increments.
                bool willBeRecycled = Lines.Length >= Lines.MaxLength;

                // Push the new line at the end (bottom of screen in buffer terms)
                Lines.Push(newLine);

                if (willBeRecycled)
                {
                    Trimmed?.Invoke(1);
                }

                // Only increment yBase if the buffer didn't recycle
                if (!willBeRecycled)
                {
                    BaseY++;
                }

                // If yDisp was at the bottom, keep it there
                if (YDisp + 1 < BaseY)
                {
                    // User was scrolled up, don't auto-scroll
                }
                else
                {
                    YDisp = BaseY;
                }
            }
            else
            {
                // Scroll region is not at top of screen.
                // Remove line from scroll region top and add blank at bottom.
                // Use yBase offset for correct absolute positioning.
                int scrollRegionStart = BaseY + ScrollTop;
                int scrollRegionEnd = BaseY + ScrollBottom;

                // Delete the line at the top of scroll region
                Lines.Splice(scrollRegionStart, 1);

                // Insert blank line at bottom of scroll region
                Lines.Splice(scrollRegionEnd, 0, newLine);
            }
        }
    }

    /// <summary>
    ///     Scrolls the buffer down by a specified number of lines.
    ///     This is reverse scrolling within the scroll region.
    /// </summary>
    public void ScrollDown(int lines)
    {
        for (int i = 0; i < lines; i++)
        {
            // Calculate absolute positions in the buffer
            int scrollRegionStart = BaseY + ScrollTop;
            int scrollRegionEnd = BaseY + ScrollBottom;

            // Remove line from scroll region bottom
            Lines.Splice(scrollRegionEnd, 1);

            // Add blank line at top of scroll region
            BufferLine newLine = GetBlankLine(AttributeData.Default);
            Lines.Splice(scrollRegionStart, 0, newLine);
        }
    }

    /// <summary>
    ///     Scrolls the display by a specified amount.
    ///     This only changes the viewport position, not the buffer content.
    /// </summary>
    public void ScrollDisp(int disp, bool suppressScrollEvent = false)
    {
        YDisp = Math.Clamp(YDisp + disp, 0, BaseY);
    }

    /// <summary>
    ///     Scrolls the viewport to show a specific line.
    /// </summary>
    /// <param name="line">The absolute line number to scroll to</param>
    public void ScrollToLine(int line)
    {
        YDisp = Math.Clamp(line, 0, BaseY);
    }

    /// <summary>
    ///     Scrolls the display to the bottom (showing active screen).
    ///     In xterm.js, yDisp = yBase means showing the active terminal area.
    /// </summary>
    public void ScrollToBottom()
    {
        YDisp = BaseY;
    }

    /// <summary>
    ///     Scrolls the display to the top.
    /// </summary>
    public void ScrollToTop()
    {
        YDisp = 0;
    }

    /// <summary>
    ///     Scrolls the viewport by a relative number of lines.
    /// </summary>
    /// <param name="lines">Number of lines to scroll (negative = up, positive = down)</param>
    public void ScrollLines(int lines)
    {
        ScrollToLine(YDisp + lines);
    }

    /// <summary>
    ///     Sets the scroll region.
    /// </summary>
    public void SetScrollRegion(int top, int bottom)
    {
        ScrollTop = Math.Clamp(top, 0, Rows - 1);
        ScrollBottom = Math.Clamp(bottom, ScrollTop, Rows - 1);
    }

    /// <summary>
    ///     Resets the scroll region to full screen.
    /// </summary>
    public void ResetScrollRegion()
    {
        ScrollTop = 0;
        ScrollBottom = Rows - 1;
    }

    /// <summary>
    ///     Gets the absolute line index for a viewport-relative y coordinate.
    /// </summary>
    public int GetAbsoluteY(int y)
    {
        return BaseY + y;
    }

    /// <summary>
    ///     Resizes the buffer.
    /// </summary>
    public void Resize(int newCols, int newRows)
    {
        // Calculate new max length keeping the same scrollback capacity
        int newMaxLength = newRows + (Lines.MaxLength - Rows);

        // Resize max length of circular list (may drop oldest lines if shrinking)
        Lines.Resize(newMaxLength);

        // Resize existing lines to the new column count
        BufferCell fillCell = BufferCell.Space;
        for (int i = 0; i < Lines.Length; i++)
        {
            Lines[i]?.Resize(newCols, fillCell);
        }

        // Ensure we have at least viewport rows available
        while (Lines.Length < newRows)
        {
            Lines.Push(new BufferLine(newCols, fillCell));
        }

        // If we have fewer rows, ensure ybase/ydisp stay in range
        BaseY = Math.Min(BaseY, Math.Max(0, Lines.Length - newRows));
        YDisp = Math.Clamp(YDisp, 0, BaseY);

        // Update scroll region and dimensions
        int oldRows = Rows;
        Cols = newCols;
        Rows = newRows;

        if (ScrollBottom == oldRows - 1)
        {
            ScrollBottom = newRows - 1;
        }
        else
        {
            ScrollBottom = Math.Min(ScrollBottom, newRows - 1);
        }

        ScrollTop = Math.Min(ScrollTop, newRows - 1);

        // Clamp cursor within new bounds
        X = Math.Clamp(X, 0, Cols - 1);
        Y = Math.Clamp(Y, 0, Rows - 1);
    }

    /// <summary>
    ///     Sets the cursor position.
    /// </summary>
    public void SetCursor(int x, int y)
    {
        X = Math.Clamp(x, 0, Cols - 1);
        Y = Math.Clamp(y, 0, Rows - 1);
    }

    /// <summary>
    ///     Moves the cursor to the specified position without any clamping.
    /// </summary>
    public void SetCursorRaw(int x, int y)
    {
        X = x;
        Y = y;
    }

    public string PrintViewport()
    {
        StringBuilder sb = new();
        for (int i = 0; i < Rows; i++)
        {
            BufferLine? line = GetLine(YDisp + i);
            if (line != null)
            {
                foreach (BufferCell cell in line)
                {
                    sb.Append(cell.Content);
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    ///     Clears the entire buffer, including scrollback and resets the cursor and scroll region.
    /// </summary>
    public void ClearScrollback()
    {
        Lines.Clear();

        BufferCell fillCell = BufferCell.Space;
        for (int i = 0; i < Rows; i++)
        {
            Lines.Push(new BufferLine(Cols, fillCell));
        }

        BaseY = 0;
        YDisp = 0;
        Y = 0;
        X = 0;

        ResetScrollRegion();
        SavedCursorState = new SavedCursor();
    }

    /// <summary>
    ///     Erases the scrollback portion of the buffer, keeping only the active screen lines.
    /// </summary>
    public void EraseScrollback()
    {
        int scrollbackLines = Lines.Length - Rows;

        if (scrollbackLines <= 0)
        {
            return;
        }

        Lines.TrimStart(scrollbackLines);

        BaseY = 0;
        YDisp = 0;
    }

    /// <summary>
    ///     Saved cursor state for DECSC/DECRC.
    /// </summary>
    public class SavedCursor
    {
        public int X { get; set; } = 0;
        public int Y { get; set; } = 0;
        public AttributeData Attr { get; set; } = AttributeData.Default;
        public CharsetMode Charset { get; set; } = CharsetMode.G0;
    }
}