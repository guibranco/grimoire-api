using Serilog;
using Serilog.Configuration;

namespace Grimoire.Api.Logging;

public static class LogStreamLoggerConfigurationExtensions
{
    /// <summary>
    /// Ships logs to a logstream-server when <paramref name="options"/> is complete and enabled;
    /// otherwise returns the configuration unchanged.
    /// </summary>
    public static LoggerConfiguration WriteToLogStream(
        this LoggerConfiguration configuration,
        LogStreamOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
            return configuration;

        return configuration.WriteTo.Sink(
            new LogStreamSink(options),
            new BatchingOptions
            {
                BatchSizeLimit = 100,
                BufferingTimeLimit = TimeSpan.FromSeconds(2),
                QueueLimit = 10_000,
                RetryTimeLimit = TimeSpan.FromMinutes(5),
            },
            restrictedToMinimumLevel: options.MinimumLevel
        );
    }
}
