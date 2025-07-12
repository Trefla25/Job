using System.Xml.Linq;
using System.Xml.XPath;

namespace TransformationConnector.Functions;

public partial class Functions
{
    public static string AddMinDateTime(XDocument xml, Dictionary<string, object?> parameters)
    {
        var xpath = parameters["XPath"]?.ToString() ?? throw new ArgumentNullException(nameof(parameters), "XPath parameter is required.");

        // Select nodes using XPath
        var nodes = xml.XPathSelectElements(xpath).ToArray();
        if (nodes.Length == 0)
        {
            return string.Empty;
        }

        // Find the minimum date
        DateTime? minDate = null;
        foreach (var node in nodes)
        {
            if (DateTime.TryParse(node.Value, out var date))
            {
                if (!minDate.HasValue || date < minDate)
                {
                    minDate = date;
                }
            }
        }

        var format = parameters.TryGetValue("Format", out var formatObj) ? formatObj?.ToString() : null;
        return minDate.HasValue ? minDate.Value.ToString(format) : string.Empty;
    }
}
