using System.Collections;
using System.Text;

namespace XTerm.Buffer;

/// <summary>
///     Represents a single line in the terminal buffer.
///     Contains an array of cells and metadata about the line.
/// </summary>
public class BufferLine : IEnumerable<BufferCell>
{
    private BufferCell[] _cells;
    private LineAttribute _lineAttribute;

    public BufferLine(int cols, BufferCell? fillCell = null)
    {
        Length = cols;
        _cells = new BufferCell[cols];
        IsWrapped = false;
        _lineAttribute = LineAttribute.Normal;

        BufferCell fill = fillCell ?? BufferCell.Space;
        for (int i = 0; i < cols; i++)
        {
            _cells[i] = fill;
        }

        Cache = null;
    }

    public int Length { get; private set; }

    public bool IsWrapped { get; set; }

    /// <summary>
    ///     Gets or sets the DEC line attribute (double-width/double-height).
    ///     Set via ESC # sequences: ESC # 3 (top), ESC # 4 (bottom), ESC # 5 (normal), ESC # 6 (double-width).
    /// </summary>
    public LineAttribute LineAttribute
    {
        get => _lineAttribute;
        set
        {
            _lineAttribute = value;
            Cache = null;
        }
    }

    /// <summary>
    ///     Returns true if this line has a double-width attribute (DECDWL or DECDHL).
    ///     Double-width lines can only display cols/2 characters.
    /// </summary>
    public bool IsDoubleWidth => _lineAttribute.IsDoubleWidth();

    /// <summary>
    ///     Cache object - this will be cleared on writes to the bufferline.
    /// </summary>
    public object? Cache { get; set; }

    /// <summary>
    ///     Gets or sets a cell at a specific column.
    /// </summary>
    public BufferCell this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
            {
                return BufferCell.Empty;
            }

            return _cells[index];
        }
        set
        {
            if (index < 0 || index >= Length)
            {
                return;
            }

            _cells[index] = value;
            Cache = null;
        }
    }

    public IEnumerator<BufferCell> GetEnumerator()
    {
        return _cells.AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return _cells.GetEnumerator();
    }

    /// <summary>
    ///     Sets a cell at a specific column.
    /// </summary>
    public void SetCell(int index, ref BufferCell cell)
    {
        if (index < 0 || index >= Length)
        {
            return;
        }

        _cells[index] = cell;
        Cache = null;
    }

    /// <summary>
    ///     Gets the cell code point at a specific column.
    /// </summary>
    public int GetCodePoint(int index)
    {
        if (index >= 0 && index < Length)
        {
            return _cells[index].CodePoint;
        }

        return 0;
    }

    /// <summary>
    ///     Resizes the line to a new column count.
    /// </summary>
    public void Resize(int cols, BufferCell fillCell)
    {
        if (cols == Length)
        {
            return;
        }

        if (cols > Length)
        {
            BufferCell[] newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, Length);
            for (int i = Length; i < cols; i++)
            {
                newCells[i] = fillCell;
            }

            _cells = newCells;
        }
        else
        {
            BufferCell[] newCells = new BufferCell[cols];
            Array.Copy(_cells, newCells, cols);
            _cells = newCells;
        }

        Cache = null;
        Length = cols;
    }

    /// <summary>
    ///     Fills a range of cells with a specific cell.
    /// </summary>
    public void Fill(BufferCell fillCell, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
        {
            endCol = Length;
        }

        for (int i = startCol; i < endCol && i < Length; i++)
        {
            _cells[i] = fillCell;
        }

        Cache = null;
    }

    /// <summary>
    ///     Copies cells from another line.
    /// </summary>
    public void CopyCellsFrom(BufferLine src, int srcCol, int destCol, int length, bool applyInReverse)
    {
        if (applyInReverse)
        {
            for (int i = length - 1; i >= 0; i--)
            {
                if (destCol + i < Length && srcCol + i < src.Length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i];
                }
            }
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                if (destCol + i < Length && srcCol + i < src.Length)
                {
                    _cells[destCol + i] = src._cells[srcCol + i];
                }
            }
        }

        Cache = null;
    }

    /// <summary>
    ///     Translates the line to a string.
    /// </summary>
    public string TranslateToString(bool trimRight = false, int startCol = 0, int endCol = -1)
    {
        if (endCol == -1)
        {
            endCol = Length;
        }

        StringBuilder sb = new();
        for (int i = startCol; i < endCol && i < Length; i++)
        {
            BufferCell cell = _cells[i];
            sb.Append(cell.Content);
        }

        return trimRight 
            ? sb.ToString().TrimEnd() 
            : sb.ToString();
    }

    /// <summary>
    ///     Gets the last non-whitespace cell index.
    /// </summary>
    public int GetTrimmedLength()
    {
        for (int i = Length - 1; i >= 0; i--)
        {
            if (!_cells[i].IsSpace() && !_cells[i].IsEmpty())
            {
                return i + 1;
            }
        }

        return 0;
    }

    /// <summary>
    ///     Clones the line.
    /// </summary>
    public BufferLine Clone()
    {
        BufferLine newLine = new(Length)
        {
            IsWrapped = IsWrapped,
            _lineAttribute = _lineAttribute
        };
        
        for (int i = 0; i < Length; i++)
        {
            newLine._cells[i] = _cells[i];
        }

        newLine.Cache = Cache;
        return newLine;
    }

    /// <summary>
    ///     Copies the line into another line.
    /// </summary>
    public void CopyFrom(BufferLine line)
    {
        if (Length != line.Length)
        {
            _cells = new BufferCell[line.Length];
            Length = line.Length;
        }

        for (int i = 0; i < Length; i++)
        {
            _cells[i] = line._cells[i];
        }

        IsWrapped = line.IsWrapped;
        _lineAttribute = line._lineAttribute;
        Cache = line.Cache;
    }
}