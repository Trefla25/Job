using eController.IntegrationHub.PlugIn;

namespace TransformationConnector.Config;

[ScriptOptions]
public class TransformationConfig
{
    public Dictionary<string, TransformationRouteConfig> TransformationRoutes { get; set; } = [];
    public bool SavePackets { get; set; }
    public string IncomingChannel { get; set; } = "Incoming";
    public string OutgoingChannel { get; set; } = "Outgoing";
    public StoreMode StoreMode { get; set; } = StoreMode.Dynamic;
}

public class TransformationRouteConfig
{
    public TransformationType Type { get; set; }
    public DestinationRoutingConfig? DestinationRouting { get; set; } = null;
    public string? SourceTopic { get; set; }
    public string? DestinationTopic { get; set; }
    public string? XsltPath { get; set; }
    public string? DestinationType { get; set; }
    public TypeConverterOptions TypeConverterOptions { get; set; } = new();
    public TransformationFunction[]? Functions { get; set; }
    public StoreMode? StoreMode { get; set; }
}

public class DestinationRoutingConfig
{
    public string? XsltPath { get; set; } = string.Empty;
    public string? DestinationXPath { get; set; } = string.Empty;
}

public enum StoreMode
{
    Persistent,
    Dynamic
}

public enum TransformationType
{
    Xslt,
    Conversion,
    Routing,
    Functions
}