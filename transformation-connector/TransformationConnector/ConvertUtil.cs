using System.Text.Json;
using System.Xml.Linq;
using TransformationConnector.Config;

namespace TransformationConnector;

public class ConvertUtil
{
    public static async ValueTask<ReadOnlyMemory<byte>> ConvertJsonToXmlAsync(Stream stream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        using var jsonDoc = await JsonDocument.ParseAsync(stream, default, cancellationToken);

        var jsonToXmlOptions = convertOptions.JsonToXmlConverter;

        XElement? rootXmlElement = null;

        if (jsonToXmlOptions.UseRootWrapping)
        {
            rootXmlElement = new XElement(jsonToXmlOptions.RootWrapperName);

            foreach (var element in jsonDoc.RootElement.EnumerateObject())
            {
                var xmlElement = new XElement(element.Name);
                rootXmlElement.Add(xmlElement);
                ConvertJsonToXmlElement(xmlElement, element.Value);
            }
        }
        else
        {
            var topLevelElement = jsonDoc.RootElement.EnumerateObject().First();
            rootXmlElement = new XElement(topLevelElement.Name);
            ConvertJsonToXmlElement(rootXmlElement, topLevelElement.Value);
        }

        var xmlDoc = new XDocument(rootXmlElement);

        using var xmlStream = new MemoryStream();
        await xmlDoc.SaveAsync(xmlStream, SaveOptions.None, cancellationToken);

        return xmlStream.ToArray().AsMemory()[3..];
    }

    private static void ConvertJsonToXmlElement(XElement xmlElement, JsonElement parentElement)
    {
        if (parentElement.ValueKind is JsonValueKind.Object)
        {
            foreach (JsonProperty property in parentElement.EnumerateObject())
            {
                var childElement = new XElement(property.Name);
                xmlElement.Add(childElement);
                ConvertJsonToXmlElement(childElement, property.Value);
            }
        }
        else if (parentElement.ValueKind is JsonValueKind.Array)
        {
            var elements = parentElement.EnumerateArray().ToArray();

            if (elements.Length == 0) { return; }

            ConvertJsonToXmlElement(xmlElement, elements[0]);

            if (elements.Length == 1) { return; }

            foreach (JsonElement element in elements[1..])
            {
                var newXmlElement = new XElement(xmlElement.Name);
                xmlElement.AddAfterSelf(newXmlElement);
                ConvertJsonToXmlElement(newXmlElement, element);
            }
        }
        else
        {
            xmlElement.SetValue(parentElement.ToString());
        }
    }

    public static async ValueTask<ReadOnlyMemory<byte>> ConvertCsvToXmlAsync(MemoryStream csvStream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream);

        var csvToXmlOptions = convertOptions.CsvToXmlConverter;

        var xmlDoc = new XDocument(new XElement(csvToXmlOptions.RootWrapperName));

        string? headerLine = await reader.ReadLineAsync(cancellationToken) ?? throw new Exception("CSV does not contain any data.");

        // Split the header into columns
        var headers = headerLine.Split(',');

        string? line;
        while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
        {
            var rowElement = new XElement(csvToXmlOptions.RowWrapperName);
            var fields = line.Split(',');

            for (int i = 0; i < headers.Length; i++)
            {
                var cellValue = i < fields.Length ? fields[i] : string.Empty; // Handle missing fields
                rowElement.Add(new XElement(headers[i], cellValue));
            }

            xmlDoc.Root?.Add(rowElement);
        }

        // Convert the XML to a byte array
        await using var xmlStream = new MemoryStream();
        await xmlDoc.SaveAsync(xmlStream, SaveOptions.None, cancellationToken);

        return xmlStream.ToArray().AsMemory();
    }

    public static async ValueTask<ReadOnlyMemory<byte>> ConvertXmlToJsonAsync(Stream stream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        var xmlDoc = XDocument.Load(stream);

        if(xmlDoc.Root is null)
        {
            throw new Exception("XML does not contain a root element.");
        }

        var xmlToJsonOptions = convertOptions.XmlToJsonConverter;

        using var memoryStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true }))
        {
            if (xmlToJsonOptions.IncludeRootWrapper)
            {
                writer.WriteStartObject();
                ConvertXmlElementToJson(writer, xmlDoc.Root);
                writer.WriteEndObject();
            }
            else
            {
                foreach (var element in xmlDoc.Root.Elements())
                {
                    writer.WriteStartObject(); // Start an object for each element
                    ConvertXmlElementToJson(writer, element);
                    writer.WriteEndObject(); // End object for each element
                }
            }
        }
        await memoryStream.FlushAsync(cancellationToken);

        return memoryStream.ToArray().AsMemory();
    }

    private static void ConvertXmlElementToJson(Utf8JsonWriter writer, XElement element)
    {
        if (element.HasElements)
        {
            writer.WriteStartObject(element.Name.LocalName);

            foreach (var child in element.Elements())
            {
                ConvertXmlElementToJson(writer, child);
            }

            writer.WriteEndObject();
        }
        else
        {
            writer.WriteString(element.Name.LocalName, element.Value);
        }
    }

    public static async ValueTask<ReadOnlyMemory<byte>> ConvertXmlToCsvAsync(Stream xmlStream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        var xmlDoc = XDocument.Load(xmlStream);

        var xmlToCsvOptions = convertOptions.XmlToCsvConverter;

        if (xmlDoc.Root is null || !xmlDoc.Root.HasElements)
        {
            throw new Exception("Can not convert to CSV because the XML document is empty.");
        }

        var rows = xmlDoc.Root.Elements(xmlToCsvOptions.RowWrapperName).ToArray();

        if (rows.Length == 0)
        {
            rows = [xmlDoc.Root];
        }

        // Extract headers from the first row's child element names
        var headers = rows[0].Elements().Select(e => e.Name.LocalName).ToList();

        using var memoryStream = new MemoryStream();
        await using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        {
            // Write headers
            await writer.WriteLineAsync(string.Join(",", headers));

            // Write each row
            foreach (var row in rows)
            {
                var fields = headers.Select(header =>
                    row.Element(header)?.Value ?? string.Empty // Handle missing elements
                );
                await writer.WriteLineAsync(string.Join(",", fields));
            }
        }

        await memoryStream.FlushAsync(cancellationToken);
        return memoryStream.ToArray().AsMemory();
    }

    public static async ValueTask<ReadOnlyMemory<byte>> ConvertJsonToCsvAsync(Stream jsonStream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        using var jsonDoc = await JsonDocument.ParseAsync(jsonStream, default, cancellationToken);

        var rows = new List<Dictionary<string, string>>();

        if (jsonDoc.RootElement.ValueKind == JsonValueKind.Array)
        {
            // Handle JSON array
            foreach (var element in jsonDoc.RootElement.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.Object)
                {
                    rows.Add(element.EnumerateObject().ToDictionary(prop => prop.Name, prop => prop.Value.ToString()));
                }
            }
        }
        else if (jsonDoc.RootElement.ValueKind == JsonValueKind.Object)
        {
            // Handle single JSON object
            rows.Add(jsonDoc.RootElement.EnumerateObject().ToDictionary(prop => prop.Name, prop => prop.Value.ToString()));
        }
        else
        {
            throw new InvalidOperationException("Unsupported JSON structure. Root must be an object or an array of objects.");
        }

        // Prepare headers
        var headers = rows.SelectMany(row => row.Keys).Distinct().ToList();

        using var memoryStream = new MemoryStream();
        await using (var writer = new StreamWriter(memoryStream, leaveOpen: true))
        {
            // Write headers
            await writer.WriteLineAsync(string.Join(",", headers));

            // Write rows
            foreach (var row in rows)
            {
                var fields = headers.Select(header => row.ContainsKey(header) ? row[header] : string.Empty);
                await writer.WriteLineAsync(string.Join(",", fields));
            }
        }

        await memoryStream.FlushAsync(cancellationToken);
        return memoryStream.ToArray().AsMemory();
    }

    public static async ValueTask<ReadOnlyMemory<byte>> ConvertCsvToJsonAsync(Stream csvStream, TypeConverterOptions convertOptions, CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(csvStream);
        string? headerLine = await reader.ReadLineAsync(cancellationToken) ?? throw new Exception("CSV does not contain any data.");

        var headers = headerLine.Split(',');

        using var memoryStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memoryStream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartArray();

            string? line;
            while ((line = await reader.ReadLineAsync(cancellationToken)) != null)
            {
                var fields = line.Split(',');
                writer.WriteStartObject();

                for (int i = 0; i < headers.Length; i++)
                {
                    var cellValue = i < fields.Length ? fields[i] : string.Empty;
                    writer.WriteString(headers[i], cellValue);
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        await memoryStream.FlushAsync(cancellationToken);
        return memoryStream.ToArray().AsMemory();
    }
}


