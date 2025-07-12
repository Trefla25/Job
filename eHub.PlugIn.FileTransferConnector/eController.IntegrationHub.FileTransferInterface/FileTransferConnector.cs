using Microsoft.Extensions.Options;
using eController.IntegrationHub.PlugIn;
using eMessenger;
using System.Runtime.InteropServices;
using System.Net.Mime;

namespace eController.IntegrationHub.FileTransferInterface;

public class FileTransferConnector(IOptions<FileTransferConnectorConfig> config, IScopedMessenger messenger, ILogger<FileTransferConnector> logger) : IPacketTransfer, IAsyncDisposable
{
    private readonly FileTransferConnectorConfig _config = config.Value;
    private readonly IScopedMessenger _messenger = messenger;
    private readonly ILogger<FileTransferConnector> _logger = logger;
    private readonly FileTransferConverter _converter = new();

    private IRegistrationToken _regToken = NullRegistrationToken.Instance;

    public IPacketConverter Converter => _converter;
    public IPacketRepository PacketRepository { get; set; } = default!;

    public event UpdateStatusDelegate? UpdateStatus;

    public async ValueTask<ProcessPacketState> ProcessPacket(PacketData packet, CancellationToken cancellationToken)
    {
        if(packet.Channel != "Incoming" && packet.Channel != "Outgoing")
        {
            _logger.LogError("Invalid channel: {Channel}", packet.Channel);
            return ProcessPacketState.FatalError;
        }
        else if (packet.Channel == "Incoming")
        {
            var metadata = _converter.GetHttpMetadata(packet.Metadata);

            if(metadata.ConfigKey is { })
            {

            }

            string incomingDirectoryPath = metadata.ConfigKey is null ? metadata.IncomingPath : _config.FileTransferIncomingRoutes[metadata.ConfigKey].IncomingPath;
            var contentType = metadata.ContentType;
            string fileExtension = GetFileExtensionFromContentType(contentType);    

            string timestamp = packet.DateCreated.ToString("yyyy-MM-dd_HH-mm-ss");

            string fileName = $"request_{timestamp}{fileExtension}";
            string filePath = Path.Combine(incomingDirectoryPath, fileName);

            try
            {
                Directory.CreateDirectory(incomingDirectoryPath);

                MemoryMarshal.TryGetArray(packet.BinaryData, out var segment);

                if (segment.Array is null)
                {
                    throw new Exception("Could not get array from Packet BinaryData");
                }

                await File.WriteAllBytesAsync(filePath, segment.Array, cancellationToken);

                _logger.LogInformation($"File {fileName} saved at {incomingDirectoryPath}.");
                return ProcessPacketState.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to save at {filePath} with content type {packet.Metadata}");
                return ProcessPacketState.FatalError;
            }
        }
        else
        {
            var metadata = _converter.GetHttpMetadata(packet.Metadata);
            string contentType = metadata.ContentType;
            
            string fileExtension = GetFileExtensionFromContentType(contentType);

            var topic = metadata.ConfigKey is null ? metadata.OutgoingTopic : _config.FileTransferOutgoingRoutes[metadata.ConfigKey].OutgoingTopic;
            var response = await _messenger.AskAsync<HttpConnectorRequestDto, HttpConnectorResponseDto>(topic, new HttpConnectorRequestDto(packet.BinaryData, null, contentType)).FirstOrDefaultResponse();
            if (response is null)
            {
                _logger.LogWarning("There was no response from the topic {Topic}", topic);
            }

            var (processedPath, errorPath) = metadata.ConfigKey is null ? (metadata.ProcessedPath, metadata.ErrorPath) : (_config.FileTransferOutgoingRoutes[metadata.ConfigKey].OutgoingProcessedPath, _config.FileTransferOutgoingRoutes[metadata.ConfigKey].OutgoingErrorPath);

            var directory = response is { } && response.IsSuccessStatusCode() ? processedPath : errorPath;
            
            Directory.CreateDirectory(directory);

            string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string fileName = $"{metadata.FileName ?? "file"}_{timestamp}{fileExtension}";
            string filePath = Path.Combine(directory, fileName);

            try
            {
                MemoryMarshal.TryGetArray(packet.BinaryData, out var segment);

                if (segment.Array is null)
                {
                    throw new Exception("Could not get array from Packet BinaryData");
                }

                await File.WriteAllBytesAsync(filePath, segment.Array, cancellationToken);

                _logger.LogInformation($"File {fileName} saved in {directory} directory.");
                return ProcessPacketState.Success;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to save {fileName} in {directory} directory.");
                return ProcessPacketState.FatalError; //de discutat daca sa fie schimbat la error, din moment ce aici intra doar daca nu merge salvat nici in proccessed, nici in error
            }
        }
    }

    public async Task Run(CancellationToken cancellationToken)
    {
        foreach (var route in _config.FileTransferIncomingRoutes)
        {
            _regToken += await _messenger.AnswerAsync<HttpConnectorRequestDto, HttpConnectorResponseDto>(route.Value.IncomingTopic, (HttpConnectorRequestDto request) => SaveRequestToPacketRepository(request, route.Key, route.Value));
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            await CheckForNewFilesAsync(cancellationToken);
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private async Task CheckForNewFilesAsync(CancellationToken cancellationToken)
    {
        foreach (var route in _config.FileTransferOutgoingRoutes)
        {
            string unProcessedFolder = route.Value.OutgoingUnprocessedPath;
            
            Directory.CreateDirectory(unProcessedFolder);

            try
            {
                var files = Directory.GetFiles(unProcessedFolder);

                foreach (var file in files)
                {
                    try
                    {
                        string fileName = Path.GetFileNameWithoutExtension(file);
                        string contentType = GetContentTypeFromFileExtension(Path.GetExtension(file));//asta mi returneaza efectiv extensia - trebuie extras mime type u sau convertit in el (switch sau dictionar - mai ok dictionar)
                        var fileContent = await File.ReadAllBytesAsync(file, cancellationToken);

                        var storeMode = route.Value.StoreMode ?? _config.StoreMode;
                        var metadata = _converter.BuildFileMetadata(
                            contentType: contentType,
                            configKey: storeMode == StoreMode.Dynamic ? route.Key : null,
                            fileName: fileName,
                            outgoingTopic: route.Value.OutgoingTopic,
                            processedPath: route.Value.OutgoingProcessedPath,
                            unprocessedPath: route.Value.OutgoingUnprocessedPath,
                            errorPath: route.Value.OutgoingErrorPath);

                        var packet = new PacketData(fileContent, "Outgoing", PacketStatus.Enqueued)
                        {
                            Metadata = metadata,
                        };

                        await PacketRepository.AddAsync(packet);

                        _logger.LogInformation($"File {file} processed and saved as packet.");
                        File.Delete(file);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to process file {file}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error while checking for files in the Incoming/Unprocessed folder.");
            }
        }
    }

    private async ValueTask<HttpConnectorResponseDto> SaveRequestToPacketRepository(HttpConnectorRequestDto request, string configKey, FileTransferIncomingRoute incomingRoute)
    {
        try
        {
            var storeMode = incomingRoute.StoreMode ?? _config.StoreMode;
            var metadata = _converter.BuildFileMetadata(
                contentType: request.ContentType,
                configKey: storeMode == StoreMode.Dynamic ? configKey : null,
                incomingPath: incomingRoute.IncomingPath);

            var packet = new PacketData(request.Content, "Incoming", PacketStatus.Enqueued)
            {
                Metadata = metadata
            };

            await PacketRepository.AddAsync(packet);

            _logger.LogInformation($"Packet with metadata saved to repository.");
            return new HttpConnectorResponseDto(null, null,202);
        }
        catch(Exception ex)
        {
            _logger.LogError(ex, $"Failed to save Packet with metadata to repository.");
            return new HttpConnectorResponseDto(null, null, 500);
        }
    }

    private string GetContentTypeFromFileExtension(string extension)
    {
        return extension switch
        {
            ".txt" => MediaTypeNames.Text.Plain,
            ".html" => MediaTypeNames.Text.Html,
            ".csv" => MediaTypeNames.Text.Csv,
            ".xml" => MediaTypeNames.Application.Xml,
            ".json" => MediaTypeNames.Application.Json,
            _ => MediaTypeNames.Application.Octet
        };
    }

    private string GetFileExtensionFromContentType(string contentType)
    {
        return contentType switch
        {
            MediaTypeNames.Text.Plain => ".txt",
            MediaTypeNames.Text.Html => ".html",
            MediaTypeNames.Text.Csv => ".csv",
            MediaTypeNames.Text.Xml => ".xml",
            MediaTypeNames.Application.Json => ".json",
            MediaTypeNames.Application.Xml => ".xml",
            _ => ".bin"
        };
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _regToken.DisposeAsync();
    }
}
