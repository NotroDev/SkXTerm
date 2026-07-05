using System.Text;
using XTerm.Buffer;

namespace XTerm.Selection;

/// <summary>
///     Selection mode for text selection.
/// </summary>
public enum SelectionMode
{
    Normal,
    Word,
    Line
}

/// <summary>
///     Manages text selection in the terminal.
/// </summary>
public class SelectionManager
{
    private readonly Terminal _terminal;
    private bool _isSelecting;
    private (int x, int y)? _selectionEnd;
    private SelectionMode _selectionMode;
    private (int x, int y)? _selectionStart;

    public SelectionManager(Terminal terminal)
    {
        _terminal = terminal;
        _isSelecting = false;
        _selectionMode = SelectionMode.Normal;
        _terminal.Buffer.Trimmed += HandleTrim;
    }

    public bool HasSelection => _selectionStart.HasValue && _selectionEnd.HasValue;

    /// <summary>
    ///     Fired when the selection changes.
    /// </summary>
    public event Action? SelectionChanged;

    /// <summary>
    ///     Starts a new selection.
    /// </summary>
    public void StartSelection(int x, int y, SelectionMode mode = SelectionMode.Normal)
    {
        _isSelecting = true;
        _selectionMode = mode;
        int absoluteY = ToAbsoluteY(y);
        _selectionStart = (x, absoluteY);
        _selectionEnd = (x, absoluteY);

        switch (mode)
        {
            // Adjust for word or line mode
            case SelectionMode.Word:
                ExpandSelectionToWord();
                break;
            case SelectionMode.Line:
                ExpandSelectionToLine();
                break;
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     Updates the selection end point.
    /// </summary>
    public void UpdateSelection(int x, int y)
    {
        if (!_isSelecting || !_selectionStart.HasValue)
        {
            return;
        }

        _selectionEnd = (x, ToAbsoluteY(y));

        switch (_selectionMode)
        {
            // Adjust for selection mode
            case SelectionMode.Word:
                ExpandSelectionToWord();
                break;
            case SelectionMode.Line:
                ExpandSelectionToLine();
                break;
        }

        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     Ends the selection.
    /// </summary>
    public void EndSelection()
    {
        _isSelecting = false;
    }

    /// <summary>
    ///     Clears the selection.
    /// </summary>
    public void ClearSelection()
    {
        _selectionStart = null;
        _selectionEnd = null;
        _isSelecting = false;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     Selects all text in the buffer.
    /// </summary>
    public void SelectAll()
    {
        _selectionStart = (0, 0);
        _selectionEnd = (_terminal.Cols - 1, Math.Max(_terminal.Buffer.Lines.Length - 1, 0));
        _isSelecting = false;
        SelectionChanged?.Invoke();
    }

    /// <summary>
    ///     Gets the selected text.
    /// </summary>
    public string GetSelectionText()
    {
        if (!HasSelection)
        {
            return string.Empty;
        }

        (int x, int y) start = _selectionStart!.Value;
        (int x, int y) end = _selectionEnd!.Value;

        // Normalize selection (start before end)
        if (start.y > end.y || (start.y == end.y && start.x > end.x))
        {
            (start, end) = (end, start);
        }

        TerminalBuffer buffer = _terminal.Buffer;
        StringBuilder text = new();

        for (int y = start.y; y <= end.y; y++)
        {
            if (y < 0 || y >= buffer.Lines.Length)
            {
                continue;
            }

            BufferLine? line = buffer.Lines[y];
            if (line == null)
            {
                continue;
            }

            int lastColumn = _terminal.Cols - 1;
            if (lastColumn < 0)
            {
                continue;
            }

            int startX = Math.Clamp(y == start.y ? start.x : 0, 0, lastColumn);
            int endX = Math.Clamp(y == end.y ? end.x : lastColumn, 0, lastColumn);

            if (startX > endX)
            {
                continue;
            }

            string lineText = line.TranslateToString(false, startX, endX + 1);
            text.Append(lineText);

            // Add line break if not last line and line doesn't wrap
            if (y < end.y && !line.IsWrapped)
            {
                text.Append('\n');
            }
        }

        return text.ToString();
    }

    /// <summary>
    ///     Checks if a cell is selected.
    /// </summary>
    public bool IsCellSelected(int x, int y)
    {
        if (!HasSelection)
        {
            return false;
        }

        int absoluteY = ToAbsoluteY(y);
        (int x, int y) start = _selectionStart!.Value;
        (int x, int y) end = _selectionEnd!.Value;

        // Normalize selection
        if (start.y > end.y || (start.y == end.y && start.x > end.x))
        {
            (start, end) = (end, start);
        }

        // Check if cell is in selection
        if (absoluteY < start.y || absoluteY > end.y)
        {
            return false;
        }

        if (absoluteY == start.y && absoluteY == end.y)
        {
            return x >= start.x && x <= end.x;
        }

        if (absoluteY == start.y)
        {
            return x >= start.x;
        }

        if (absoluteY == end.y)
        {
            return x <= end.x;
        }

        return true;
    }

    /// <summary>
    ///     Expands selection to word boundaries.
    /// </summary>
    private void ExpandSelectionToWord()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue)
        {
            return;
        }

        TerminalBuffer buffer = _terminal.Buffer;
        (int x, int y) start = _selectionStart.Value;
        (int x, int y) end = _selectionEnd.Value;

        // Expand start to word boundary
        BufferLine? startLine = start.y >= 0 && start.y < buffer.Lines.Length ? buffer.Lines[start.y] : null;
        if (startLine != null)
        {
            while (start.x > 0 && IsWordChar(startLine[start.x - 1].Content))
            {
                start.x--;
            }
        }

        // Expand end to word boundary
        BufferLine? endLine = end.y >= 0 && end.y < buffer.Lines.Length ? buffer.Lines[end.y] : null;
        if (endLine != null)
        {
            while (end.x < _terminal.Cols - 1 && IsWordChar(endLine[end.x + 1].Content))
            {
                end.x++;
            }
        }

        _selectionStart = start;
        _selectionEnd = end;
    }

    /// <summary>
    ///     Expands selection to line boundaries.
    /// </summary>
    private void ExpandSelectionToLine()
    {
        if (!_selectionStart.HasValue || !_selectionEnd.HasValue)
        {
            return;
        }

        (int x, int y) start = _selectionStart.Value;
        (int x, int y) end = _selectionEnd.Value;

        // Normalize
        if (start.y > end.y)
        {
            (start, end) = (end, start);
        }

        // Select entire lines
        start.x = 0;
        end.x = _terminal.Cols - 1;

        _selectionStart = start;
        _selectionEnd = end;
    }

    /// <summary>
    ///     Checks if a character is a word character.
    /// </summary>
    private bool IsWordChar(string ch)
    {
        if (string.IsNullOrEmpty(ch))
        {
            return false;
        }

        char c = ch[0];
        return char.IsLetterOrDigit(c) || c == '_';
    }

    private int ToAbsoluteY(int viewportY)
    {
        return _terminal.Buffer.YDisp + viewportY;
    }

    private void HandleTrim(int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        if (_selectionStart.HasValue)
        {
            _selectionStart = (_selectionStart.Value.x, _selectionStart.Value.y - amount);
        }

        if (_selectionEnd.HasValue)
        {
            _selectionEnd = (_selectionEnd.Value.x, _selectionEnd.Value.y - amount);
        }

        if (_selectionEnd is { y: < 0 })
        {
            ClearSelection();
            return;
        }

        if (_selectionStart is { y: < 0 })
        {
            _selectionStart = (0, 0);
        }

        if (_selectionEnd.HasValue)
        {
            int maxY = Math.Max(_terminal.Buffer.Lines.Length - 1, 0);
            _selectionEnd = (_selectionEnd.Value.x, Math.Min(_selectionEnd.Value.y, maxY));
        }

        SelectionChanged?.Invoke();
    }
}