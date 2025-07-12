using eController.IntegrationHub.PlugIn;
using System.Text.Json;

namespace eController.IntegrationHub.FileTransferInterface;

public class FileTransferConverter : IPacketConverter
{
    public string BuildFileMetadata(string contentType, string? configKey = null, string? fileName = null, string? incomingPath = null, string? outgoingTopic = null, string? processedPath = null, string? unprocessedPath = null, string? errorPath = null)
    {
        var fileMetadata = new FileMetadata
        {
            FileName = fileName,
            ContentType = contentType,
            IncomingPath = incomingPath,
            OutgoingTopic = outgoingTopic,
            ProcessedPath = processedPath,
            UnprocessedPath = unprocessedPath,
            ErrorPath = errorPath,
            ConfigKey = configKey
        };

        return JsonSerializer.Serialize(fileMetadata);
    }

    public virtual FileMetadata GetHttpMetadata(string metadata)
    {
        var httpMetadata = JsonSerializer.Deserialize<FileMetadata>(metadata) ?? throw new Exception("Could not deserialize metadata");
        return httpMetadata;
    }
}

/// <summary>
/// Class representing file metadata, similar to HTTP metadata.
/// </summary>
public class FileMetadata
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string IncomingPath { get; set; } = string.Empty;
    public string OutgoingTopic { get; set; } = string.Empty;
    public string ProcessedPath { get; set; } = string.Empty;
    public string UnprocessedPath { get; set; } = string.Empty;
    public string ErrorPath { get; set; } = string.Empty;
    public string? ConfigKey { get; set; }
}