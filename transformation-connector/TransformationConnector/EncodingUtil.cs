using System.Net.Mime;
using System.Text;

namespace TransformationConnector;

public static class EncodingUtil
{
    public static ReadOnlyMemory<byte> TrimBom(ReadOnlyMemory<byte> bytes, string? contentType)
    {
        var encoding = TryGetEncoding(contentType) ?? Encoding.UTF8;
        var preamble = encoding.GetPreamble();

        if (bytes.Span.StartsWith(preamble))
        {
            return bytes[preamble.Length..];
        }

        return bytes;

    }
    private static Encoding? TryGetEncoding(string? contentType)
    {
        if (contentType is null)
        {
            return null;
        }

        try
        {
            var charset = new ContentType(contentType).CharSet;
            if (charset is null)
            {
                return null;
            }

            return Encoding.GetEncoding(charset);
        }
        catch
        {
            return null;
        }
    }
}
