using System.Xml.Linq;
using System.Xml.XPath;
using eController.Util.Serialization;

namespace TransformationConnector.Functions;

public partial class Functions
{
    public static string AddMaxDateTime(XDocument xml, Dictionary<string, object?> parameters)
    {
        var xpath = parameters["XPath"]?.ToString() ?? throw new ArgumentNullException(nameof(parameters), "XPath parameter is required.");

        // Select nodes using XPath
        var nodes = xml.XPathSelectElements(xpath).ToArray();
        if (nodes.Length == 0)
        {
            return string.Empty;
        }

        // Find the maximum date
        DateTime? maxDate = null;
        foreach (var node in nodes)
        {
            if (DateTime.TryParse(node.Value, out var date))
            {
                if (!maxDate.HasValue || date > maxDate)
                {
                    maxDate = date;
                }
            }
        }

        if(parameters.TryGetValue("Delay", out var delayObj) && HumanTimeSpan.Parse(delayObj?.ToString()) is { } delay)
        {
            maxDate = maxDate?.Add(delay);
        }

        var format = parameters.TryGetValue("Format", out var formatObj) ? formatObj?.ToString() : null;
        return maxDate.HasValue ? maxDate.Value.ToString(format) : string.Empty;
    }
}
