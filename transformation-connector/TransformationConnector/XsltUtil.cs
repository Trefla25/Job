using System.Runtime.InteropServices;
using System.Xml.Xsl;
using System.Xml;

namespace TransformationConnector;

public class XsltUtil
{
    public static async Task<ReadOnlyMemory<byte>> TransformXML(ReadOnlyMemory<byte> xml, string xsltPath)
    {
        if (!MemoryMarshal.TryGetArray(xml, out var segment) || segment.Array is null)
        {
            throw new Exception("Can not create stream from xml content data.");
        }

        await using var memoryStream = new MemoryStream(segment.Array);

        var transform = new XslCompiledTransform();
        transform.Load(xsltPath);

        memoryStream.Seek(0, SeekOrigin.Begin);
        await using var outputStream = new MemoryStream();
        transform.Transform(XmlReader.Create(memoryStream), null, outputStream);

        return outputStream.ToArray().AsMemory();
    }
}