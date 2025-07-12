using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Xml.Linq;
using System.Xml.XPath;
using TransformationConnector.Config;

namespace TransformationConnector.Functions;

public class TransformationFunctionsEngine(ILoggerFactory loggerFactory)
{
    private readonly ILogger<TransformationFunctionsEngine> _logger = loggerFactory.CreateLogger<TransformationFunctionsEngine>();

    public ReadOnlyMemory<byte> ApplyFunctions(ReadOnlyMemory<byte> xmlBytes, IEnumerable<TransformationFunction> functions)
    {
        if (!MemoryMarshal.TryGetArray(xmlBytes, out var segment) || segment.Array is null)
        {
            throw new Exception("Can not create stream from xml content data.");
        }

        using var memoryStream = new MemoryStream(segment.Array);
        var xml = XDocument.Load(memoryStream);

        foreach (var function in functions)
        {
            try
            {
                var method = typeof(Functions).GetMethod(function.Name, BindingFlags.Public | BindingFlags.Static)
                ?? throw new InvalidOperationException($"Function '{function.Name}' not found.");

                var parameters = method.GetParameters();
                if (parameters.Length != 2 || parameters[0].ParameterType != typeof(XDocument) || parameters[1].ParameterType != typeof(Dictionary<string, object?>))
                {
                    throw new InvalidOperationException($"Function '{function.Name}' has an invalid signature.");
                }

                var result = method.Invoke(null, [xml, function.Parameters])?.ToString();

                UpdateOrAddNode(xml, function.TargetNode, result ?? "");
            }
            catch(Exception ex)
            {
                _logger.LogError(ex, "Error applying function '{functionName}'.", function.Name);
            }
        }

        using var writeStream = new MemoryStream();
        xml.Save(writeStream);

        return writeStream.GetBuffer().AsMemory(0, (int)writeStream.Length);
    }

    public void UpdateOrAddNode(XDocument xml, string xpath, string nodeValue)
    {
        // Try to find the node using XPath
        var existingNode = xml.XPathSelectElement(xpath);

        if (existingNode != null)
        {
            // Update the existing node's value
            existingNode.Value = nodeValue;
            return;
        }

        // Split the XPath into segments to create missing nodes
        var segments = xpath.Split(['/'], StringSplitOptions.RemoveEmptyEntries);
        var currentNode = xml.Root ?? throw new Exception("The XML document is missing the root element.");

        foreach (var segment in segments)
        {
            if(segment == xml.Root.Name.LocalName)
            {
                continue;
            }

            // Try to find the next node in the path
            var nextNode = currentNode.Element(segment);
            if (nextNode is null)
            {
                nextNode = new XElement(segment);
                currentNode.Add(nextNode);
            }

            // Move to the next node in the path
            currentNode = nextNode;
        }

        currentNode.Value = nodeValue;
    }
}
