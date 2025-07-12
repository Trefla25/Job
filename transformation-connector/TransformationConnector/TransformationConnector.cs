using eController.IntegrationHub.PlugIn;
using eMessenger;
using Microsoft.Extensions.Options;
using System.Text;
using System.Net.Mime;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Xml;
using System.Xml.Linq;
using System.Xml.XPath;
using TransformationConnector.Config;
using TransformationConnector.Functions;

namespace TransformationConnector;

public class TransformationConnector(
    IScopedMessenger messenger,
    IOptions<TransformationConfig> options,
    ILoggerFactory loggerFactory,
    ILogger<TransformationConfig> logger)
    : IPacketTransfer, IAsyncDisposable
{
    private readonly IScopedMessenger _messenger = messenger;
    private readonly TransformationConfig _config = options.Value;
    private readonly ILogger<TransformationConfig> _logger = logger;
    private readonly TransformationFunctionsEngine _functionsEngine = new(loggerFactory);
    private readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private IRegistrationToken _regToken = NullRegistrationToken.Instance;

    public IPacketConverter Converter { get; set; } = new PacketConverter();
    public IPacketRepository PacketRepository { get; set; } = default!;

    public event UpdateStatusDelegate? UpdateStatus;

    public async Task Run(CancellationToken cancellationToken)
    {
        foreach (var route in _config.TransformationRoutes)
        {
            var transformKey = route.Key;
            var transform = route.Value;

            if (transform.SourceTopic is null)
            {
                _logger.LogWarning("Source topic missing.");
                continue;
            }

            _regToken += await _messenger.AnswerAsync<HttpConnectorRequestDto, HttpConnectorResponseDto>(
                transform.SourceTopic,
                (request) => HandleMessage(request, transformKey, transform));
        }
    }
   
    public async ValueTask<ProcessPacketState> ProcessPacket(PacketData packet, CancellationToken cancellationToken)
    {
        var packets = new List<PacketData>();
        try
        {
            var metadata = GetPacketMetadata(packet);
            var routeConfig = GetTransformationRouteConfig(metadata);
            var startingStage = metadata switch
            {
                _ when packet.Channel == _config.IncomingChannel && metadata.ContentType != MediaTypeNames.Application.Xml => TransformationStage.XMLFormatConversion,
                _ when packet.Channel == _config.IncomingChannel && metadata.ContentType == MediaTypeNames.Application.Xml => TransformationStage.XSLTTransformation,
                _ when packet.Channel == _config.OutgoingChannel && metadata.ContentType != routeConfig.DestinationType => TransformationStage.DestinationFormatConversion,
                _ when packet.Channel == _config.OutgoingChannel && metadata.ContentType == routeConfig.DestinationType => TransformationStage.DestinationForwarding,
                _ => throw new Exception($"Can not determine what to do with the packet {packet.Id}.")
            };

            for (TransformationStage currentStage = startingStage; currentStage < TransformationStage.DestinationForwarding; currentStage++)
            {
                var newPacket = currentStage switch
                {
                    TransformationStage.XMLFormatConversion => await ConvertToContentType(packet, MediaTypeNames.Application.Xml),
                    TransformationStage.XSLTTransformation => await ApplyXslt(packet),
                    TransformationStage.DestinationFormatConversion => await ConvertToContentType(packet, routeConfig.DestinationType),
                    _ => throw new NotImplementedException("Unknown transformation stage")
                };

                packets.Add(newPacket);
                await UpdatePacketStatus(packet, PacketStatus.Processed);
                packet = newPacket;
            }

            var destinationMetadata = GetPacketMetadata(packet);
            var destinationRouteConfig = GetTransformationRouteConfig(destinationMetadata);

            var response = await SendToDestination(packet, destinationRouteConfig.DestinationTopic, destinationMetadata.ContentType);

            if (!response.IsSuccessStatusCode())
            {
                await UpdatePacketStatus(packet, PacketStatus.FatalError);
                // TODO: Maybe add a child packet with the error message if the status is not success?
                // Here the response message  is lost, while in HandleMessage it is returned, but if we only implement here we lose consistency
                throw new Exception($"Request failed with status code: {response.StatusCode} and content: {Encoding.UTF8.GetString(response.Content.Span)}");
            }

            await UpdatePacketStatus(packet, PacketStatus.Processed);
            return ProcessPacketState.Success;
        }
        catch(Exception ex)
        {
            foreach(var inProgress in packets.Where(p => p.Status == PacketStatus.InProgress))
            {
                await UpdatePacketStatus(packet, PacketStatus.FatalError);
            }

            _logger.LogError(ex, "Error processing packet {PacketId}", packet.Id);
            return ProcessPacketState.FatalError;
        }
    }

    private ValueTask<PacketData> InsertPaket(PacketData packet) => _config.SavePackets ? PacketRepository.AddAsync(packet) : ValueTask.FromResult(packet);
    
    private Task UpdatePacketStatus(PacketData packet, PacketStatus status)
    {
        packet.Status = status;
        return _config.SavePackets? PacketRepository.UpdatePacketStatusAsync(packet.Id, status) : Task.CompletedTask;
    }

    private async ValueTask<PacketData> ConvertToContentType(PacketData packet, string? contentType, ConnectorMetadata? metadata = null, TransformationRouteConfig? routeConfig = null)
    {
        metadata ??= GetPacketMetadata(packet);
        routeConfig ??= GetTransformationRouteConfig(metadata);

        if(contentType is null || contentType == metadata.ContentType)
        {
            return packet;
        }

        MemoryMarshal.TryGetArray(packet.BinaryData, out var dataArray);
        var contentStream = new MemoryStream(dataArray.Array ?? throw new Exception("Could not get byte array from binary data."));

        var content = metadata.ContentType switch
        {
            _ when contentType is MediaTypeNames.Application.Xml && metadata.ContentType is MediaTypeNames.Application.Json => await ConvertUtil.ConvertJsonToXmlAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ when contentType is MediaTypeNames.Application.Xml && metadata.ContentType is MediaTypeNames.Text.Csv => await ConvertUtil.ConvertCsvToXmlAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ when contentType is MediaTypeNames.Application.Json && metadata.ContentType is MediaTypeNames.Application.Xml => await ConvertUtil.ConvertXmlToJsonAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ when contentType is MediaTypeNames.Text.Csv && metadata.ContentType is MediaTypeNames.Application.Xml => await ConvertUtil.ConvertXmlToCsvAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ when contentType is MediaTypeNames.Text.Csv && metadata.ContentType is MediaTypeNames.Application.Json => await ConvertUtil.ConvertJsonToCsvAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ when contentType is MediaTypeNames.Application.Json && metadata.ContentType is MediaTypeNames.Text.Csv => await ConvertUtil.ConvertCsvToJsonAsync(contentStream, routeConfig.TypeConverterOptions ?? new()),
            _ => throw new NotImplementedException($"Can not automatically convert type {metadata.ContentType} into {contentType}")
        };

        content = EncodingUtil.TrimBom(content, Encoding.UTF8.WebName);
        metadata.ContentType = contentType;

        var newPacket = new PacketData()
        {
            BinaryData = content,
            Channel = _config.OutgoingChannel,
            Metadata = JsonSerializer.Serialize(metadata, JsonSerializerOptions),
            Status = PacketStatus.InProgress
        };

        return await InsertPaket(newPacket);
    }

    private async ValueTask<PacketData> ApplyFunctions(PacketData packet, ConnectorMetadata? metadata = null, TransformationRouteConfig? routeConfig = null)
    {
        metadata ??= GetPacketMetadata(packet);
        routeConfig ??= GetTransformationRouteConfig(metadata);

        if (routeConfig.Functions is not { Length: > 0})
        {
            return packet;
        }

        var functionsXml = _functionsEngine.ApplyFunctions(packet.BinaryData, routeConfig.Functions);
        functionsXml = EncodingUtil.TrimBom(functionsXml, Encoding.UTF8.WebName);

        var newPacket = new PacketData()
        {
            BinaryData = functionsXml,
            Channel = _config.OutgoingChannel,
            Metadata = JsonSerializer.Serialize(metadata, JsonSerializerOptions),
            Status = PacketStatus.InProgress
        };

        return await InsertPaket(newPacket);
    }

    private async ValueTask<PacketData> ApplyXslt(PacketData packet, ConnectorMetadata? metadata = null, TransformationRouteConfig? routeConfig = null)
    {
        metadata ??= GetPacketMetadata(packet);
        routeConfig ??= GetTransformationRouteConfig(metadata);

        if (string.IsNullOrEmpty(routeConfig.XsltPath))
        {
            return packet;
        }

        var transformedXML = await XsltUtil.TransformXML(packet.BinaryData, routeConfig.XsltPath);
        transformedXML = EncodingUtil.TrimBom(transformedXML, Encoding.UTF8.WebName);

        var newPacket = new PacketData()
        {
            BinaryData = transformedXML,
            Channel = _config.OutgoingChannel,
            Metadata = JsonSerializer.Serialize(metadata, JsonSerializerOptions),
            Status = PacketStatus.InProgress
        };

        return await InsertPaket(newPacket);
    }

    private async ValueTask<HttpConnectorResponseDto> SendToDestination(PacketData packet, string? destinationTopic, string? destinationType)
    {
        if (destinationTopic is null)
        {
            throw new Exception("Destination topic is null");
        }

        return await _messenger.AskAsync<HttpConnectorRequestDto, HttpConnectorResponseDto>(
                destinationTopic,
                new HttpConnectorRequestDto(packet.BinaryData, destinationTopic, destinationType))
                .FirstOrDefaultResponse()
                ?? throw new Exception("The response was null");
    }

    private TransformationRouteConfig GetTransformationRouteConfig(ConnectorMetadata metadata)
    => metadata.TransformKey is null
        ? new TransformationRouteConfig
        {
            DestinationTopic = metadata.DestinationTopic,
            XsltPath = metadata.XsltPath,
            DestinationType = metadata.DestinationType,
            TypeConverterOptions = metadata.TypeConverterOptions ?? new(),
        }
        : _config.TransformationRoutes[metadata.TransformKey];

    private ConnectorMetadata GetPacketMetadata(PacketData packet)
        => JsonSerializer.Deserialize<ConnectorMetadata>(packet.Metadata ?? "", JsonSerializerOptions) ?? throw new Exception("Could not deserialize metadata or metadata is null");

    private string BuildPacketMetadata(string transformKey, TransformationRouteConfig routeConfig, string contentType)
    {
        var storeMode = routeConfig.StoreMode ?? _config.StoreMode;

        var connectorMetadata = storeMode == StoreMode.Persistent
            ? new ConnectorMetadata()
            {
                DestinationTopic = routeConfig.DestinationTopic,
                XsltPath = routeConfig.XsltPath,
                ContentType = contentType,
                DestinationType = routeConfig.DestinationType,
                TypeConverterOptions = routeConfig.TypeConverterOptions,
            }
            : new ConnectorMetadata()
            {
                ContentType = contentType,
                TransformKey = transformKey
            };

        return JsonSerializer.Serialize(connectorMetadata, JsonSerializerOptions);
    }

    private async ValueTask<HttpConnectorResponseDto> HandleMessage(HttpConnectorRequestDto request, string transformKey, TransformationRouteConfig transformConfig)
    {
        PacketData? initialPacket = null, xmlPacket = null, transformedPacket = null, destinationPacket = null, functionsPacket = null;
        string? destinationTopic = transformConfig.DestinationTopic, destinationType = null;

        try
        {
            if(request.ContentType is null)
            {
                return new HttpConnectorResponseDto(
                    Encoding.UTF8.GetBytes("Request content type is null!"), 
                    MediaTypeNames.Text.Plain, 
                    StatusCodes.Status415UnsupportedMediaType);
            }

            var metadataString = BuildPacketMetadata(transformKey, transformConfig, request.ContentType);
            initialPacket = await InsertPaket(new PacketData
            {
                BinaryData = request.Content,
                Channel = _config.IncomingChannel,
                Metadata = metadataString,
                Status = PacketStatus.InProgress
            });

            var metadata = GetPacketMetadata(initialPacket);

            switch (transformConfig.Type)
            {
                case TransformationType.Xslt:
                    xmlPacket = await ConvertToContentType(initialPacket, MediaTypeNames.Application.Xml, metadata, transformConfig);
                    await UpdatePacketStatus(initialPacket, PacketStatus.Processed);
                    metadata.ContentType = MediaTypeNames.Application.Xml;

                    transformedPacket = await ApplyXslt(xmlPacket, metadata, transformConfig);
                    await UpdatePacketStatus(xmlPacket, PacketStatus.Processed);

                    functionsPacket = await ApplyFunctions(transformedPacket, metadata, transformConfig);
                    await UpdatePacketStatus(transformedPacket, PacketStatus.Processed);

                    destinationPacket = await ConvertToContentType(functionsPacket, transformConfig.DestinationType, metadata, transformConfig);
                    await UpdatePacketStatus(functionsPacket, PacketStatus.Processed);
                    destinationType = transformConfig.DestinationType ?? metadata.ContentType;
                    break;

                case TransformationType.Conversion:
                    destinationPacket = await ConvertToContentType(initialPacket, transformConfig.DestinationType, metadata, transformConfig);
                    await UpdatePacketStatus(initialPacket, PacketStatus.Processed);
                    destinationType = transformConfig.DestinationType ?? metadata.ContentType;
                    break;

                case TransformationType.Routing:
                    xmlPacket = await ConvertToContentType(initialPacket, MediaTypeNames.Application.Xml, metadata, transformConfig);
                    await UpdatePacketStatus(initialPacket, PacketStatus.Processed);
                    metadata.ContentType = MediaTypeNames.Application.Xml;

                    var transformedXml = await XsltUtil.TransformXML(xmlPacket.BinaryData, transformConfig.DestinationRouting?.XsltPath ?? throw new Exception($"Missing Destination XsltPath in the {transformKey} route's configuration."));
                    
                    destinationTopic = ExtractDestinationTopic(transformedXml, transformConfig.DestinationRouting?.DestinationXPath ?? throw new Exception($"Missing Destination XPath in the {transformKey} route's configuration."));

                    destinationPacket = xmlPacket;
                    destinationType = MediaTypeNames.Application.Xml;
                    break;

                case TransformationType.Functions:
                    xmlPacket = await ConvertToContentType(initialPacket, MediaTypeNames.Application.Xml, metadata, transformConfig);
                    await UpdatePacketStatus(initialPacket, PacketStatus.Processed);
                    metadata.ContentType = MediaTypeNames.Application.Xml;

                    destinationPacket = await ApplyFunctions(xmlPacket, metadata, transformConfig);
                    await UpdatePacketStatus(xmlPacket, PacketStatus.Processed);

                    destinationType = MediaTypeNames.Application.Xml;
                    break;
            }

            if(destinationPacket is null)
            {
                throw new Exception("Could not create destination packet.");
            }

            var httpResponse = await SendToDestination(destinationPacket, destinationTopic, destinationType);

            if (!httpResponse.IsSuccessStatusCode())
            {
                var errorMessage = Encoding.UTF8.GetString(httpResponse.Content.Span);
                await HandleFatalError(destinationPacket, errorMessage);
            }
            else
            {
                await UpdatePacketStatus(destinationPacket, PacketStatus.Processed);
            }

            return httpResponse;
        }
        catch (Exception ex)
        {
            if(initialPacket?.Status == PacketStatus.InProgress)
            {
                await HandleFatalError(initialPacket, ex.Message);
            }
            if (xmlPacket?.Status == PacketStatus.InProgress)
            {
                await HandleFatalError(xmlPacket, ex.Message);
            }
            if (transformedPacket?.Status == PacketStatus.InProgress)
            {
                await HandleFatalError(transformedPacket, ex.Message);
            }
            if (functionsPacket?.Status == PacketStatus.InProgress)
            {
                await HandleFatalError(functionsPacket, ex.Message);
            }
            if (destinationPacket?.Status == PacketStatus.InProgress)
            {
                await HandleFatalError(destinationPacket, ex.Message);
            }

            _logger.LogError(ex, "Error handling the message");
            var errorMessage = "Error handling the message";
            return new HttpConnectorResponseDto(
                Encoding.UTF8.GetBytes(errorMessage), 
                MediaTypeNames.Text.Plain,
                StatusCodes.Status500InternalServerError);
        }
    }

    private async Task HandleFatalError(PacketData packet, string errorMessage)
    {
        await UpdatePacketStatus(packet, PacketStatus.FatalError);

        var childPacket = new PacketData
        {
            BinaryData = Encoding.UTF8.GetBytes(errorMessage),
            ParentId = packet.Id,
            Channel = $"{packet.Channel}:Error",
            Status = PacketStatus.FatalError
        };

        await InsertPaket(childPacket);
    }

    private string ExtractDestinationTopic(ReadOnlyMemory<byte> transformedXml, string destinationXPath)
    {
        using var memoryStream = new MemoryStream(transformedXml.ToArray());
        
        var xDoc = XDocument.Load(memoryStream);
        var destinationElement = xDoc.XPathSelectElement(destinationXPath);

        return destinationElement?.Value ?? string.Empty;
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await _regToken.DisposeAsync();
    }
}
