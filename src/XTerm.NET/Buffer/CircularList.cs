namespace XTerm.Buffer;

/// <summary>
///     Circular list implementation for terminal buffer lines.
///     Provides efficient wraparound behavior for scrollback buffer.
/// </summary>
public class CircularList<T>(int maxLength)
    where T : class
{
    private T?[] _array = new T?[maxLength];
    private int _startIndex = 0;

    public int MaxLength { get; private set; } = maxLength;

    public int Length { get; private set; } = 0;

    public T? this[int index]
    {
        get
        {
            if (index < 0 || index >= Length)
            {
                throw new IndexOutOfRangeException();
            }

            return _array[GetCyclicIndex(index)];
        }
        set
        {
            if (index < 0 || index >= Length)
            {
                throw new IndexOutOfRangeException();
            }

            _array[GetCyclicIndex(index)] = value;
        }
    }

    /// <summary>
    ///     Pushes a new item to the end of the list.
    /// </summary>
    public void Push(T item)
    {
        if (Length == MaxLength)
        {
            // Overwrite oldest item
            _array[GetCyclicIndex(Length)] = item;
            _startIndex = (_startIndex + 1) % MaxLength;
        }
        else
        {
            _array[GetCyclicIndex(Length)] = item;
            Length++;
        }
    }

    /// <summary>
    ///     Removes and returns the last item.
    /// </summary>
    public T? Pop()
    {
        if (Length == 0)
        {
            return null;
        }

        int index = GetCyclicIndex(Length - 1);
        T? item = _array[index];
        _array[index] = null;
        Length--;
        return item;
    }

    /// <summary>
    ///     Inserts items at a specific index.
    /// </summary>
    public void Splice(int start, int deleteCount, params T[] items)
    {
        if (start < 0 || start > Length)
        {
            throw new IndexOutOfRangeException();
        }

        // Remove items
        if (deleteCount > 0)
        {
            for (int i = start; i < Length - deleteCount; i++)
            {
                this[i] = this[i + deleteCount];
            }

            Length -= deleteCount;
        }

        // Insert items
        foreach (T item in items)
        {
            if (Length < MaxLength)
            {
                // First increase length
                Length++;

                // Then shift items right from the end
                for (int i = Length - 1; i > start; i--)
                {
                    this[i] = this[i - 1];
                }

                // Now insert the item
                this[start] = item;
                start++;
            }
            else
            {
                // At max capacity, push out oldest
                Push(item);
            }
        }
    }

    /// <summary>
    ///     Trims the list to a specific length.
    /// </summary>
    public void TrimStart(int count)
    {
        if (count <= 0)
        {
            return;
        }

        count = Math.Min(count, Length);
        _startIndex = (_startIndex + count) % MaxLength;
        Length -= count;
    }

    /// <summary>
    ///     Shifts the start index by a specified amount.
    /// </summary>
    public void ShiftElements(int start, int count, int direction)
    {
        switch (direction)
        {
            case > 0:
            {
                // Shift right
                for (int i = count - 1; i >= 0; i--)
                {
                    if (start + i + direction < Length)
                    {
                        this[start + i + direction] = this[start + i];
                    }
                }

                break;
            }
            case < 0:
            {
                // Shift left
                for (int i = 0; i < count; i++)
                {
                    if (start + i + direction >= 0)
                    {
                        this[start + i + direction] = this[start + i];
                    }
                }

                break;
            }
        }
    }

    /// <summary>
    ///     Recycles a line from the buffer, or creates a new one.
    /// </summary>
    public T? Recycle()
    {
        return Length >= MaxLength 
            ? Pop() 
            : null;
    }

    /// <summary>
    ///     Gets the actual array index for a logical index.
    /// </summary>
    private int GetCyclicIndex(int index)
    {
        return (_startIndex + index) % MaxLength;
    }

    /// <summary>
    ///     Clears the list.
    /// </summary>
    public void Clear()
    {
        Array.Clear(_array, 0, _array.Length);
        _startIndex = 0;
        Length = 0;
    }

    /// <summary>
    ///     Resizes the maximum length of the circular list.
    /// </summary>
    public void Resize(int newMaxLength)
    {
        if (newMaxLength == MaxLength)
        {
            return;
        }

        T?[] newArray = new T?[newMaxLength];
        int copyLength = Math.Min(Length, newMaxLength);

        for (int i = 0; i < copyLength; i++)
        {
            newArray[i] = this[i];
        }

        _array = newArray;
        MaxLength = newMaxLength;
        _startIndex = 0;
        Length = copyLength;
    }

    /// <summary>
    ///     Gets an enumerable of all items.
    /// </summary>
    public IEnumerable<T> GetItems()
    {
        for (int i = 0; i < Length; i++)
        {
            T? item = this[i];
            if (item != null)
            {
                yield return item;
            }
        }
    }
}