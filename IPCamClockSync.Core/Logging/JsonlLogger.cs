using System.Text.Json;

namespace IPCamClockSync.Core.Logging;

public sealed class JsonlLogger
{
    private readonly string _logFilePath;

    public JsonlLogger(string logDirectory, string fileName = "app.jsonl")
    {
        Directory.CreateDirectory(logDirectory);
        _logFilePath = Path.Combine(logDirectory, fileName);
    }

    public void Write(string level, string channel, string message, string correlationId)
    {
        var logEntry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            level,
            channel,
            message,
            correlationId,
        };

        var line = JsonSerializer.Serialize(logEntry);
        File.AppendAllLines(_logFilePath, new[] { line });
    }
}
