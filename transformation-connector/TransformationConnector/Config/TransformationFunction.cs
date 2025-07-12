namespace TransformationConnector.Config;

public class TransformationFunction
{
    public string Name { get; set; } = "";
    public Dictionary<string, object?> Parameters { get; set; } = [];
    public string TargetNode { get; set; } = "";
}
