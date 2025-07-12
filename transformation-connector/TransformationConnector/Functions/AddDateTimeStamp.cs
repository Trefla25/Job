using System.Xml.Linq;

namespace TransformationConnector.Functions;

public partial class Functions
{
    public static string AddDateTimeStamp(XDocument xml, Dictionary<string, object?> parameters)
    {
        var format = parameters.TryGetValue("Format", out var formatObj) ? formatObj?.ToString() : null;
        return DateTime.Now.ToString(format);
    }
}
