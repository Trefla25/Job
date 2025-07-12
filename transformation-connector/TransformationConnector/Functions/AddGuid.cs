using System.Xml.Linq;

namespace TransformationConnector.Functions;

public partial class Functions
{
    public static string AddGuid(XDocument xml, Dictionary<string, object?> parameters)
    {
        return Guid.NewGuid().ToString();
    }
}
