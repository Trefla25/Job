using eController.IntegrationHub.PlugIn;

namespace eController.IntegrationHub.FileTransferInterface;

[ScriptOptions]
public class FileTransferConnectorConfig
{
    public StoreMode StoreMode { get; set; } = StoreMode.Dynamic;

    /// <summary>
    /// Collection of all the topics from which the File Transfer Connector recieves requests
    /// </summary>
    public Dictionary<string, FileTransferIncomingRoute> FileTransferIncomingRoutes { get; set; } = [];

    /// <summary>
    /// Collection of all the topics to which the File Transfer Connector sends requests
    /// </summary>
    public Dictionary<string, FileTransferOutgoingRoute> FileTransferOutgoingRoutes { get; set; } = [];
}

public class FileTransferIncomingRoute
{
    public string? IncomingTopic { get; set; }
    public string? IncomingPath { get; set; }
    public StoreMode? StoreMode { get; set; }
}

public class FileTransferOutgoingRoute
{
    public string? OutgoingTopic { get; set; }
    public string? OutgoingUnprocessedPath { get; set; }
    public string? OutgoingProcessedPath { get; set; }
    public string? OutgoingErrorPath { get; set; }
    public StoreMode? StoreMode { get; set; }
}

public enum StoreMode
{
    Persistent,
    Dynamic
}