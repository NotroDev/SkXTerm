using System.Text;
using XTerm.Common;
using XTerm.Events.Parser;

namespace XTerm.Parser;

/// <summary>
///     VT100/ANSI escape sequence parser implementing a state machine.
///     Based on Paul Williams' ANSI parser state machine.
/// </summary>
public sealed class EscapeSequenceParser
{
    private readonly StringBuilder _collect = new();
    private readonly StringBuilder _dcs = new();
    private readonly StringBuilder _osc = new();
    private readonly Params _params = new();
    private ParserState _state = ParserState.Ground;

    // Parser events - Standard C# event pattern
    /// <summary>
    ///     Fired when printable characters are parsed.
    /// </summary>
    public event EventHandler<PrintEventArgs>? Print;

    /// <summary>
    ///     Fired when control characters are executed.
    /// </summary>
    public event EventHandler<ExecuteEventArgs>? Execute;

    /// <summary>
    ///     Fired when CSI sequences are parsed.
    /// </summary>
    public event EventHandler<CsiEventArgs>? Csi;

    /// <summary>
    ///     Fired when ESC sequences are parsed.
    /// </summary>
    public event EventHandler<EscEventArgs>? Esc;

    /// <summary>
    ///     Fired when OSC sequences are parsed.
    /// </summary>
    public event EventHandler<OscEventArgs>? Osc;

    /// <summary>
    ///     DCS parsing is not implemented yet. This event is retained for source compatibility.
    /// </summary>
#pragma warning disable CS0067
    [Obsolete("DCS parsing is not implemented yet; this event is retained for source compatibility.")]
    public event EventHandler<DcsEventArgs>? Dcs;
#pragma warning restore CS0067

    /// <summary>
    ///     Parses input data byte by byte.
    /// </summary>
    public void Parse(string data)
    {
        foreach (Rune rune in data.EnumerateRunes())
        {
            ParseChar(rune.Value);
        }
    }

    /// <summary>
    ///     Parses a single character/code point.
    /// </summary>
    private void ParseChar(int code)
    {
        ParserState currentState = _state;

        // C0/C1 control characters
        if (code is < 0x20 or >= 0x80 and < 0xA0)
        {
            switch (currentState)
            {
                case ParserState.Ground:
                case ParserState.Escape:
                case ParserState.CsiEntry:
                case ParserState.CsiParam:
                case ParserState.CsiIntermediate:
                case ParserState.CsiIgnore:
                    OnExecute(code);
                    if (code == 0x1B) // ESC
                    {
                        Transition(ParserState.Escape);
                    }

                    return;

                case ParserState.OscString:
                    switch (code)
                    {
                        // ESC or BEL
                        case 0x1B or 0x07:
                            DispatchOsc();
                            Transition(code == 0x1B ? ParserState.Escape : ParserState.Ground);
                            break;
                        case >= 0x20:
                            OscPut(code);
                            break;
                    }

                    return;
            }
        }

        // Normal state machine processing
        switch (_state)
        {
            case ParserState.Ground:
                if (code >= 0x20)
                {
                    OnPrint(code);
                }

                break;

            case ParserState.Escape:
                switch (code)
                {
                    case 0x5B: // [
                        Transition(ParserState.CsiEntry);
                        break;
                    case 0x5D: // ]
                        Transition(ParserState.OscString);
                        break;
                    case 0x50: // P
                        Transition(ParserState.DcsEntry);
                        break;
                    case 0x5E: // ^
                    case 0x5F: // _
                    case 0x58: // X
                        Transition(ParserState.SosPmApcString);
                        break;
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        Transition(ParserState.EscapeIntermediate);
                        break;
                    case >= 0x30 and < 0x7F:
                        DispatchEsc(code);
                        Transition(ParserState.Ground);
                        break;
                    default:
                        Transition(ParserState.Ground);
                        break;
                }

                break;

            case ParserState.EscapeIntermediate:
                switch (code)
                {
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        break;
                    case >= 0x30 and < 0x7F:
                        DispatchEsc(code);
                        Transition(ParserState.Ground);
                        break;
                }

                break;

            case ParserState.CsiEntry:
                switch (code)
                {
                    // Private parameter markers (<, =, >, ?)
                    case >= 0x3C and <= 0x3F:
                        Collect(code);
                        break;
                    // 0-9, :, ;
                    case >= 0x30 and < 0x3C:
                        Param(code);
                        Transition(ParserState.CsiParam);
                        break;
                    case >= 0x40 and < 0x7F:
                        DispatchCsi(code);
                        Transition(ParserState.Ground);
                        break;
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        Transition(ParserState.CsiIntermediate);
                        break;
                }

                break;

            case ParserState.CsiParam:
                switch (code)
                {
                    case >= 0x30 and < 0x40:
                        Param(code);
                        break;
                    case >= 0x40 and < 0x7F:
                        DispatchCsi(code);
                        Transition(ParserState.Ground);
                        break;
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        Transition(ParserState.CsiIntermediate);
                        break;
                }

                break;

            case ParserState.CsiIntermediate:
                switch (code)
                {
                    case >= 0x20 and < 0x30:
                        Collect(code);
                        break;
                    case >= 0x40 and < 0x7F:
                        DispatchCsi(code);
                        Transition(ParserState.Ground);
                        break;
                }

                break;

            case ParserState.CsiIgnore:
                if (code is >= 0x40 and < 0x7F)
                {
                    Transition(ParserState.Ground);
                }

                break;

            case ParserState.OscString:
                OscPut(code);
                break;

            case ParserState.DcsEntry:
            case ParserState.DcsParam:
            case ParserState.DcsIgnore:
            case ParserState.DcsPassthrough:
                // DCS handling (simplified)
                if (code is 0x9C or 0x1B) // ST or ESC
                {
                    Transition(ParserState.Ground);
                }

                break;
        }
    }

    private void Transition(ParserState newState)
    {
        // Exit actions
        switch (_state)
        {
            case ParserState.CsiEntry:
            case ParserState.CsiParam:
            case ParserState.CsiIntermediate:
            case ParserState.CsiIgnore:
                if (newState != ParserState.CsiParam && newState != ParserState.CsiIntermediate &&
                    newState != ParserState.CsiIgnore)
                {
                    _params.Reset();
                    _collect.Clear();
                }

                break;
        }

        _state = newState;

        // Entry actions
        switch (newState)
        {
            case ParserState.CsiEntry:
            case ParserState.DcsEntry:
                _params.Reset();
                _collect.Clear();
                _params.AddParam(0);
                break;

            case ParserState.OscString:
                _osc.Clear();
                break;
        }
    }

    /// <summary>
    ///     Raises the Print event.
    /// </summary>
    private void OnPrint(int code)
    {
        Print?.Invoke(this, new PrintEventArgs(char.ConvertFromUtf32(code)));
    }

    /// <summary>
    ///     Raises the Execute event.
    /// </summary>
    private void OnExecute(int code)
    {
        Execute?.Invoke(this, new ExecuteEventArgs(code));
    }

    private void Collect(int code)
    {
        _collect.Append((char)code);
    }

    private void Param(int code)
    {
        switch (code)
        {
            // ;
            case 0x3B:
                _params.AddParam(0);
                break;
            // 0-9
            case >= 0x30 and <= 0x39:
            {
                int digit = code - 0x30;

                // Get current value of last parameter and update it
                int currentValue = _params.GetParam(_params.Length - 1);
                int newValue = (currentValue * 10) + digit;
                _params.UpdateLastParam(newValue);
                break;
            }
        }
    }

    private void DispatchCsi(int code)
    {
        string finalChar = ((char)code).ToString();
        // Clone params so handlers get their own copy
        Params paramsClone = _params.Clone();
        // Collected characters come BEFORE the final character (e.g., "?" before "h" gives "?h")
        string identifier = _collect + finalChar;
        OnCsi(identifier, paramsClone);
    }

    /// <summary>
    ///     Raises the Csi event.
    /// </summary>
    private void OnCsi(string identifier, Params parameters)
    {
        Csi?.Invoke(this, new CsiEventArgs(identifier, parameters));
    }

    private void DispatchEsc(int code)
    {
        string finalChar = ((char)code).ToString();
        OnEsc(finalChar, _collect.ToString());
    }

    /// <summary>
    ///     Raises the Esc event.
    /// </summary>
    private void OnEsc(string finalChar, string collected)
    {
        Esc?.Invoke(this, new EscEventArgs(finalChar, collected));
    }

    private void OscPut(int code)
    {
        _osc.Append(char.ConvertFromUtf32(code));
    }

    private void DispatchOsc()
    {
        OnOsc(_osc.ToString());
    }

    /// <summary>
    ///     Raises the Osc event.
    /// </summary>
    private void OnOsc(string data)
    {
        Osc?.Invoke(this, new OscEventArgs(data));
    }

    /// <summary>
    ///     Resets the parser to initial state.
    /// </summary>
    public void Reset()
    {
        _state = ParserState.Ground;
        _params.Reset();
        _collect.Clear();
        _osc.Clear();
        _dcs.Clear();
    }
}