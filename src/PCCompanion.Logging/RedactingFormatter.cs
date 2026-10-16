using System.Globalization;
using System.IO;
using Serilog.Events;
using Serilog.Formatting;

namespace PCCompanion.Logging;

/// <summary>
/// Formats a log entry with another formatter, then redacts the whole text, including exceptions.
/// </summary>
internal sealed class RedactingFormatter(ITextFormatter inner, PathRedactor redactor) : ITextFormatter
{
    public void Format(LogEvent logEvent, TextWriter output)
    {
        using var buffer = new StringWriter(CultureInfo.InvariantCulture);
        inner.Format(logEvent, buffer);
        output.Write(redactor.Redact(buffer.ToString()));
    }
}
