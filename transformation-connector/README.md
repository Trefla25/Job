# Overview

The **Transformation Connector** serves as a powerful intermediary that listens for incoming messages from a source connector, transforms them using multiple configurable transformation routes, and forwards the transformed data to a destination connector. By using advanced transformations, routing, and conversions, it ensures data is in the desired format and properly routed before being passed to its next destination.

# How It Works

The Transformation Connector processes messages using configurable routes that define specific transformations or actions to apply. For most transformations, messages pass through the following generalized steps:

1. Converts the Source Message to XML (if it is not already in XML format).
2. Determines the route **Type** and applies the transformation, function, or conversion as defined in the route configuration.
3. Converts the transformed XML to the destination format, if necessary.
4. Forwards the transformed message to a Destination Connector on a destination topic.

## Flow Diagram

The Transformation Connector processes messages using a flexible and modular approach. The general flow ensures that incoming messages are processed according to their configuration and forwarded in the correct format to a destination topic.

![image](https://github.com/user-attachments/assets/8dc77fa9-1b9b-4983-9706-bbbbe9c2dbd2)

***

One of the key strengths of the Transformation Connector is the ability to chain routes. This means the destination topic of one route can serve as the source topic for the next, allowing you to split complex transformations into smaller, modular steps.

Example Chained Flow:

![image](https://github.com/user-attachments/assets/6a4176d7-d7e8-4752-85dd-0aa89a87ae05)

## Route Types

The **Transformation Connector** supports multiple **route types**, each serving a distinct purpose.

### Conversion Route

If you just need to convert a message from one format to another, this is the simplest route. No XSLT or complex transformations – just specify the desired output format.

The messages pass trough the following steps:

- Converts the message directly to the specified type (e.g., CSV to JSON, JSON to XML).
- Sends the converted message to the destination topic.

Configuration Example:

```json
{
  "Type": "Conversion",
  "SourceTopic": "SourceConnector.ToConversion",
  "DestinationTopic": "DestinationConnector.FromConversion",
  "DestinationType": "text/csv"
}
```

### Routing (Redirection) Route

Sometimes you don’t need to change the message itself – you just need to figure out where it should go. The Redirection Route takes care of this by using an XSLT file to dynamically determine the destination topic.

The messages pass trough the following steps:

- Converts the message to XML (if it’s not already).
- Applies the XSLT transformation to extract the destination topic.
- Forwards the message (as XML) to the extracted destination topic.

Configuration Example:

```json
{
  "Type": "Routing",
  "SourceTopic": "SourceConnector.ToRouting",
  "DestinationRouting": {
    "XsltPath": "C:\\Path\\To\\Xslt\\Routing.xslt",
    "DestinationXpath": "/Routing/DestinationTopic"
  }
}

```

### XSLT Transformation Route

This is the classic route – it’s where most transformations happen. Use it when you need to apply an XSLT to transform a message into a new format.

The messages pass trough the following steps:

- Converts the message to XML (if it’s not already).
- Applies the specified XSLT transformation.
- Converts the transformed XML to the desired output format (e.g., JSON, CSV) if specified.
- Sends the final message to the destination topic.

Configuration Example:

```json
{
  "Type": "Xslt",
  "SourceTopic": "SourceConnector.ToXslt",
  "DestinationTopic": "DestinationConnector.FromXslt",
  "XsltPath": "C:\\Path\\To\\Xslt\\TransformationFile.xslt",
  "DestinationType": "application/xml",
}

```

### Functions Route

Sometimes you need to go beyond what standard XSLT can achieve. While XSLT is powerful for transformations, it has limitations when handling advanced operations like dynamic calculations or custom logic. Traditionally, this has been done using **CDATA** sections to embed scripts, but this approach is considered unsafe.

To address this, the Function Route introduces a safer alternative: a set of [Predefined Functions](docId:6YdpegDUH6P6U8Md5W1In). Functions can be applied directly to XML nodes, and the result will either overwrite an existing node or create a new one.

The messages pass trough the following steps:

- Converts the message to XML (if it’s not already).
- Runs the configured function with the specified parameters in order.
- Writes each of the function’s result to a specific XML node. If the node doesn’t exist, it’s created.
- Sends the updated XML message to the destination topic.

Configuration Example:

```json
{
  "Type": "Functions",
  "SourceTopic": "SourceConnector.ToFunctions",
  "DestinationTopic": "DestinationConnector.FromFunctions",
  "Functions": [
    {
	  "Name": "AddDateTimeStamp",
	  "Parameters": {
	    "Format": "yyyy-MM-dd HH:mm:ss"
	  },
	  "TargetNode": "//DATETIME"
    },
    {
	  "Name": "AddGuid",
	  "Parameters": { },
	  "TargetNode": "//TRACEID"
    }
  ]
}

```

The **Xslt** transformation type can aslo apply configured functions, but these functions will be called on the XML resulted after the XSLT transformation.

## Conversion Rules

Conversions ensure messages are transformed between formats like JSON, XML, and CSV seamlessly. Some conversion rules are flexible and can be customized per route, allowing fine-grained control over how data is structured during these transformations.

Each route type can use the **Type Converter Options** to configure these rules. If no custom options are provided, the default conversion rules will be applied.

Here are the default conversion behaviors:

### JSON to XML

By default, when converting from JSON to XML:

- The entire JSON object is wrapped in a root element called `ROOT`.
- Each JSON property is converted into an XML element under the root.

**Example Input (JSON):**

```json
{
  "Name": "Apple",
  "Price": 2.50
}
```

**Output (XML):**

```xml
<ROOT>
    <Name>Apple</Name>
    <Price>2.50</Price>
</ROOT>
```

### XML to JSON

When converting from XML to JSON:

- The root element and all its child nodes are converted into JSON properties.
- By default, the root element is included as part of the converted JSON.

**Example Input (XML):**

```xml
<ROOT>
    <Name>Apple</Name>
    <Price>2.50</Price>
</ROOT>
```

**Output (JSON):**

```json
{
  "ROOT": {
    "Name": "Apple",
    "Price": 2.50
  }
}
```

### CSV to XML

When converting from CSV to XML:

- The first row of the CSV is used to determine the XML element names.
- Each subsequent row becomes a group of elements wrapped under a default root element `ROOT`, and each column corresponds to a row-level XML child `ROW`.

**Example Input (CSV):**

```
Name,Price
Apple,2.50
Banana,1.20
```

**Output (XML):**

```xml
<ROOT>
    <ROW>
        <Name>Apple</Name>
        <Price>2.50</Price>
    </ROW>
    <ROW>
        <Name>Banana</Name>
        <Price>1.20</Price>
    </ROW>
</ROOT>
```

### XML to CSV

When converting from CSV to XML:

- The child elements of the first `ROW` element determine the column headers (first row of the CSV).
- If no rows are found, the root's child elements are treated as a single row.

**Example Input (XML with rows):**

```xml
<ROOT>
    <ROW>
        <Name>Apple</Name>
        <Price>2.50</Price>
    </ROW>
    <ROW>
        <Name>Banana</Name>
        <Price>1.20</Price>
    </ROW>
</ROOT>
```

**Output (CSV):**

```
Name,Price
Apple,2.50
Banana,1.20
```

**Example Input (XML with no rows):**

```xml
<ROOT>
    <Name>Apple</Name>
    <Price>2.50</Price>
</ROOT>
```

**Output (CSV):**

```
Name,Price
Apple,2.50
```

### JSON to CSV

When converting from JSON to CSV:

- The JSON object’s keys (first level only) are used to determine the column headers.
- The input can be either:
  - An array of objects, where each object becomes a row in the CSV.
  - A single JSON object, which is treated as a single row.

**Example Input (JSON - Single Object):**

```json
{
  "Name": "Apple",
  "Price": 2.50
}
```

**Output (CSV):**

```
Name,Price
Apple,2.50
```

**Example Input (JSON - Array):**

```json
[
  {
    "Name": "Apple",
    "Price": 2.50
  },
  {
    "Name": "Banana",
    "Price": 1.20
  }
]
```

**Output (CSV):**

```
Name,Price
Apple,2.50
Banana,1.20
```

### CSV to JSON

When converting from CSV to JSON:

- The first row of the CSV is used as the keys for JSON objects.
- Each subsequent row becomes an individual JSON object.

**Example Input (CSV):**

```
Name,Price
Apple,2.50
Banana,1.20
```

**Output (JSON):**

```json
[
  {
    "Name": "Apple",
    "Price": 2.50
  },
  {
    "Name": "Banana",
    "Price": 1.20
  }
]
```

# Configuration

The following JSON illustrates how to configure the Transformation Connector:

```json
{
  "TransformationConnector": {
    "Type": "TransformationConnector",
    "Enabled": true,
    "PacketTransfer": {
      "DbPath": "C:\\Path\\To\\Your\\Database\\TransformationConnector.db",
      "ChannelGroups": {
        "Outgoing": {
          "DbPollInterval": "3s",
          "PacketsPerCycle": 10,
          "PacketRetention": "30d",
          "CleanerInterval": "1h",
          "CanResend": false,
          "Channels": ["TransformationToDestination"]
        },
        "Incoming": {
          "DbPollInterval": "3s",
          "PacketsPerCycle": 10,
          "PacketRetention": "30d",
          "CleanerInterval": "1h",
          "CanResend": false,
          "Channels": ["SourceToTransformation"]
        }
      }
    },

    "Config": {
      "TransformationRoutes": {
        "ConversionExampleRoute": {
          "Type": "Conversion",
          "SourceTopic": "SourceConnector.ToConversion",
          "DestinationTopic": "DestinationConnector.FromConversion",
          "DestinationType": "application/json",

          "TypeConverterOptions": {
            "XmlToJsonConverter": {
              "IncludeRootWrapper": true
            }
          }
        },
        
        "XsltExampleRoute": {
          "Type": "Xslt",
          "SourceTopic": "SourceConnector.ToXslt",
          "DestinationTopic": "DestinationConnector.FromXslt",
          "XsltPath": "C:\\Path\\To\\Xslt\\TransformationFile.xslt",
          "DestinationType": "application/xml",

          "TypeConverterOptions": {
            "JsonToXmlConverter": {
              "UseRootWrapping": true,
              "RootWrapperName": "Root"
            },
            "CsvToXmlConverter": {
              "RootWrapperName": "Root",
              "RowWrapperName": "Row"
            }
          }
        },
        
        "FunctionsExampleRoute": {
          "Type": "Functions",
          "SourceTopic": "SourceConnector.ToFunctions",
          "DestinationTopic": "DestinationConnector.FromFunctions",
          "Functions": [
		    {
		      "Name": "AddDateTimeStamp",
			    "Parameters": {
			      "Format": "yyyy-MM-dd HH:mm:ss"
			    },
			    "TargetNode": "//DATETIME"
		    }
          ],
          "TypeConverterOptions": {
            "JsonToXmlConverter": {
              "UseRootWrapping": true,
              "RootWrapperName": "Root"
            }
          }
        },
        
        "RoutingExampleRoute": {
          "Type": "Routing",
          "SourceTopic": "SourceConnector.ToRouting",
          "DestinationRouting": {
            "XsltPath": "C:\\Path\\To\\Xslt\\Routing.xslt",
            "DestinationXpath": "/Routing/DestinationTopic"
          },
          "TypeConverterOptions": {
            "JsonToXmlConverter": {
              "UseRootWrapping": false
            }
          }
        }
      },
      "SavePackets": true,
      "IncomingChannel": "SourceToTransformation",
      "OutgoingChannel": "TransformationToDestination"
    }
  }
}
```

## Transformation Connector Configuration

All specific configurations for the Transformation Connector are located under the `Config` property.

| Property           |   Type            |   Description                                                                                                                                                        |
| ---------------------- | ----------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TransformationRoutes` | `key-value pairs` | A list of transformation routes defined as key-value pairs, where each route specifies the transformation from a source topic to a destination using XSLT transform. |
| `SavePackets`          | `boolean`         | Determines whether the connector should store received messages in the database for logging or debugging purposes.                                                   |
| `IncomingChannel`      | `string`          | The channel used to store the initial message in the database.                                                                                                       |
| `OutgoingChannel`      | `string`          | The channel used to store the transformed message in the database.                                                                                                   |

### Transformation Route Config

|   Property             |   Type   |   Description                                                                                                     |
| ---------------------- | -------- | ----------------------------------------------------------------------------------------------------------------- |
| `Type`                 | `string` | Specifies the type of transformation route. Supported values are `Routing`, `Xslt`, `Conversion`, or `Functions`. |
| `SourceTopic`          | `string` | The topic from which the connector receives incoming messages.                                                    |
| `DestinationTopic`     | `string` | The topic to which the connector sends transformed messages.                                                      |
| `XsltPath`             | `string` | The file path of the XSLT transformation file used to transform the XML messages.                                 |
| `DestinationType`      | `string` | The MIME type of the transformed data, specifying the content type of the output.                                 |
| `DestinationRouting`   | `object` | Specifies additional routing logic to determine the destination topic dynamically.                                |
| `Functions`            | `array`  | A list of functions to apply to the XML message.                                                                  |
| `TypeConverterOptions` | `object` | Options for converting between JSON, XML, and CSV formats. This options are detailed in the tables below.         |

### Destination Routing Options

|   Property          |   Type   |   Description                                                                 |
| ------------------- | -------- | ----------------------------------------------------------------------------- |
| `XsltPath `         | `string` | The file path of the XSLT file used to generate the routing XML.              |
| `DestinationXPath ` | `string` | The XPath query used to extract the DestinationTopicfrom the transformed XML. |

### Function Options

| Property     | Type              | Description                                                                                                               |
| ------------ | ----------------- | ------------------------------------------------------------------------------------------------------------------------- |
| `Name`       | `string`          | The name of the predefined function to execute.                                                                           |
| `Parameters` | `key-value pairs` | A dictionary used as input parameters for the function. Keys are function-specific.                                       |
| `TargetNode` | `string`          | The XPath location in the XML where the function’s result will be written. If the node doesn’t exist, it will be created. |

### Type Converter Options&#x20;

|   Property           |   Type   |   Description                        |
| -------------------- | -------- | ------------------------------------ |
| `JsonToXmlConverter` | `object` | Settings for JSON-to-XML conversion. |
| `XmlToJsonConverter` | `object` | Settings for XML-to-JSON conversion. |
| `CsvToXmlConverter`  | `object` | Settings for CSV-to-XML conversion.  |
| `XmlToCsvConverter`  | `object` | Settings for XML-to-CSV conversion.  |

**JsonToXmlConverter**  

|   Property         |   Type    |   Description                                                              |
| ------------------ | --------- | -------------------------------------------------------------------------- |
| `UseRootWrapping ` | `boolean` | Specifies whether to wrap the root element when converting JSON to XML.    |
| `RootWrapperName ` | `string`  | The name of the root wrapper used when `UseRootWrapping` is set to `true`. |

**XmlToJsonConverter**  

|   Property           |   Type    |   Description                                                                      |
| -------------------- | --------- | ---------------------------------------------------------------------------------- |
| `IncludeRootWrapper` | `boolean` | Determines whether to include the root wrapper in the JSON output when converting. |

**CsvToXmlConverter**  

|   Property        |   Type   |   Description                                                                     |
| ----------------- | -------- | --------------------------------------------------------------------------------- |
| `RootWrapperName` | `string` | Defines the name of the root wrapper used in the XML structure during conversion. |
| `RowWrapperName ` | `string` | Specifies the name of the row wrapper used when converting CSV data to XML.       |

**XmlToCsvConverter**  

| Property          |   Type   |   Description                                                           |
| ----------------- | -------- | ----------------------------------------------------------------------- |
| `RowWrapperName ` | `string` | Defines the name of the row wrapper used when extracting rows from XML. |

## Packet Transfer Configuration

All specific configurations for the packet transfer process within the Transformation Connector are located under the `PacketTransfer` property.

For a detailed guide on PacketTransfer configurations, please refer to the [PacketTransfer](docId:-AVlVgAL5miY0fKm3IjRG) Guide.
