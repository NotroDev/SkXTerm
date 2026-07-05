using XTerm.Parser;

namespace XTerm.Events.Parser;

/// <summary>
///     Event arguments for print operations.
/// </summary>
public class PrintEventArgs(string data) : EventArgs
{
    /// <summary>
    ///     The character(s) to print.
    /// </summary>
    public string Data { get; } = data;
}

/// <summary>
///     Event arguments for control character execution.
/// </summary>
public class ExecuteEventArgs(int code) : EventArgs
{
    /// <summary>
    ///     The control character code.
    /// </summary>
    public int Code { get; } = code;
}

/// <summary>
///     Event arguments for CSI (Control Sequence Introducer) sequences.
/// </summary>
public class CsiEventArgs(string identifier, Params parameters) : EventArgs
{
    /// <summary>
    ///     The CSI sequence identifier (final character and any collected intermediates).
    /// </summary>
    public string Identifier { get; } = identifier;

    /// <summary>
    ///     The parameters for the CSI sequence.
    /// </summary>
    public Params Parameters { get; } = parameters;
}

/// <summary>
///     Event arguments for ESC sequences.
/// </summary>
public class EscEventArgs(string finalChar, string collected) : EventArgs
{
    /// <summary>
    ///     The final character of the ESC sequence.
    /// </summary>
    public string FinalChar { get; } = finalChar;

    /// <summary>
    ///     Any collected intermediate characters.
    /// </summary>
    public string Collected { get; } = collected;
}

/// <summary>
///     Event arguments for OSC (Operating System Command) sequences.
/// </summary>
public class OscEventArgs(string data) : EventArgs
{
    /// <summary>
    ///     The OSC command data.
    /// </summary>
    public string Data { get; } = data;
}

/// <summary>
///     Event arguments for DCS (Device Control String) sequences.
/// </summary>
public class DcsEventArgs(string data, Params parameters) : EventArgs
{
    /// <summary>
    ///     The DCS command data.
    /// </summary>
    public string Data { get; } = data;

    /// <summary>
    ///     The parameters for the DCS sequence.
    /// </summary>
    public Params Parameters { get; } = parameters;
}