namespace TransformationConnector.Config
{
    public class TypeConverterOptions
    {
        public JsonToXmlConverter JsonToXmlConverter { get; set; } = new();
        public XmlToJsonConverter XmlToJsonConverter { get; set; } = new();
        public CsvToXmlConverter CsvToXmlConverter { get; set; } = new();
        public XmlToCsvConverter XmlToCsvConverter { get; set; } = new();
    }

    public class JsonToXmlConverter
    {
        public bool UseRootWrapping { get; set; } = true;
        public string RootWrapperName { get; set; } = "ROOT";
    }

    public class XmlToJsonConverter
    {
        public bool IncludeRootWrapper { get; set; } = true;
    }

    public class CsvToXmlConverter
    {
        public string RootWrapperName { get; set; } = "ROOT";
        public string RowWrapperName { get; set; } = "ROW";
    }

    public class XmlToCsvConverter
    {
        public string RowWrapperName { get; set; } = "ROW";
    }
}
