using Serilog;
using Serilog.Configuration;

namespace Grimoire.Api.Logging;

public static class LogStreamLoggerConfigurationExtensions
{
    /// <summary>
    /// Ships logs to a logstream-server through the <c>Serilog.Sinks.LogStream</c> sink when
    /// <paramref name="options"/> is complete and enabled; otherwise returns the configuration
    /// unchanged.
    /// </summary>
    public static LoggerConfiguration WriteToLogStream(
        this LoggerConfiguration configuration,
        LogStreamOptions options
    ) => configuration.WriteToLogStream(options, messageHandler: null);

    internal static LoggerConfiguration WriteToLogStream(
        this LoggerConfiguration configuration,
        LogStreamOptions options,
        HttpMessageHandler? messageHandler
    )
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(options);

        if (!options.IsConfigured)
            return configuration;

        return configuration.WriteTo.LogStream(
            sink =>
            {
                sink.ServerUrl = options.ServerUri;
                sink.ApiKey = options.ApiKey!.Trim();
                sink.ApiToken = options.ApiToken!.Trim();
                sink.AppId = string.IsNullOrWhiteSpace(options.AppId)
                    ? AppVersion.Current
                    : options.AppId.Trim();
                sink.UserAgent = $"Grimoire.Api/{AppVersion.Current}";
                sink.RequestTimeout = TimeSpan.FromSeconds(10);
                sink.MessageHandler = messageHandler;
            },
            batching =>
            {
                batching.BatchSizeLimit = 100;
                batching.BufferingTimeLimit = TimeSpan.FromSeconds(2);
                batching.QueueLimit = 10_000;
                batching.RetryTimeLimit = TimeSpan.FromMinutes(5);
            },
            restrictedToMinimumLevel: options.MinimumLevel
        );
    }
}
