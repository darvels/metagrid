using System.Text.Json;
using MetaGrid.Core.Abstractions;
using MetaGrid.Core.Models;

namespace MetaGrid.Infrastructure.Services;

public sealed class FileLoggingService(IAppPaths appPaths) : ILoggingService
{
    public string GetLogsDirectory() => appPaths.LogsDirectory;

    public async Task LogAsync(LogLevelKind level, string message, object? data = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(appPaths.LogsDirectory);
        var logFile = Path.Combine(appPaths.LogsDirectory, $"{DateTime.UtcNow:yyyy-MM-dd}.log");
        var record = new
        {
            timestamp = DateTimeOffset.UtcNow,
            level = level.ToString(),
            message,
            data
        };

        var json = JsonSerializer.Serialize(record);
        await File.AppendAllTextAsync(logFile, json + Environment.NewLine, cancellationToken);
    }
}
