using TransformationConnector.Config;

namespace TransformationConnector;

public class ConnectorMetadata
{
    public string? DestinationTopic { get; set; }
    public string? XsltPath { get; set; }
    public required string ContentType { get; set; }
    public string? DestinationType { get; set; }
    public TypeConverterOptions? TypeConverterOptions { get; set; }
    public string? TransformKey { get; set; }
}
