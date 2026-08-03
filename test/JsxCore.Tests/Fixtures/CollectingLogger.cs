using Microsoft.Extensions.Logging;

namespace JsxCore.Tests.Fixtures;

/// <summary>Collects formatted warnings, for the tests that assert something was explained.</summary>
public sealed class CollectingLogger<T>(List<string> warnings) : ILogger<T>
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (IsEnabled(logLevel))
        {
            warnings.Add(formatter(state, exception));
        }
    }
}
