using eController.IntegrationHub.PlugIn;
using eController.IntegrationHub.PlugIn.UI;
using System.Net.Mime;
using System.Text;
using System.Text.Json;

namespace TransformationConnector
{
    public class PacketConverter : IPacketConverter
    {
        public ValueTask<UIConversionInfo> PacketToUIDataConverter(PacketData packet, CancellationToken cancellation = default)
        {
            var data = Encoding.UTF8.GetString(packet.BinaryData.Span);

            if (packet.Channel.EndsWith("Error"))
            {
                return ValueTask.FromResult(new UIConversionInfo(data, data, UIDataTypes.Plaintext));
            }

            var dataPreview = data.Length < 20 ? data : data[..20];

            if (packet.Metadata is null)
            {
                throw new Exception("Missing metadata information.");
            }

            var connectorMetadata = JsonSerializer.Deserialize<ConnectorMetadata>(packet.Metadata)
                ?? throw new Exception("Failed to deserialize metadata. Metadata is null.");

            var contentType = connectorMetadata?.ContentType;

            var contentData = contentType switch
            {
                MediaTypeNames.Application.Json => new UIConversionInfo(data, dataPreview, UIDataTypes.Json),
                MediaTypeNames.Application.Xml => new UIConversionInfo(data, dataPreview, UIDataTypes.Xml),
                MediaTypeNames.Text.Csv => new UIConversionInfo(data, dataPreview, UIDataTypes.Plaintext),
                MediaTypeNames.Text.Plain => new UIConversionInfo(data, dataPreview, UIDataTypes.Plaintext),
                _ => throw new NotImplementedException($"Can not create conversion info for {contentType}")
            };

            return ValueTask.FromResult(contentData);
        }
    }
}
