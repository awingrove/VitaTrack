using Microsoft.Extensions.Logging;

namespace VitaTrack.Tests.TestDoubles;

/// <summary>
/// An <see cref="ILogger{T}"/> that keeps what it was told instead of writing it
/// anywhere, so a test can assert on the entries. Exists because a "did it log, and
/// what did it say" assertion is only writable this way — and because the assertions
/// that matter for a logging line are usually about what it does <em>not</em> contain:
/// a probe carries a credential, so the log line is a place a secret can leak, and
/// "the line exists" is the easy half.
/// </summary>
internal sealed class RecordingLogger<T> : ILogger<T>
{
    private readonly List<LogEntry> _entries = [];

    public IReadOnlyList<LogEntry> Entries => _entries;

    /// <summary>The formatted text of every entry at <paramref name="level"/>, joined.
    /// Formatted rather than the raw template so an assertion reads the line an
    /// operator would see, which is the thing that must not contain a secret.</summary>
    public string TextAt(LogLevel level) => string.Join(
        " | ",
        _entries.Where(e => e.Level == level).Select(e => e.Message));

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        _entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
}

/// <summary>One captured call: the level, the formatted message, and the exception the
/// caller attached. The exception is kept because "logged the type name only, not the
/// object" is a claim about what was <em>not</em> passed, and only the captured call
/// can settle it.</summary>
internal sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
