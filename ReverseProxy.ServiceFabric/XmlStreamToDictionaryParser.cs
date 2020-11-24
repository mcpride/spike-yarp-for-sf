using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal class XmlStreamToDictionaryParserOptions
    {
        public IEnumerable<string> Parents { get; set; }
        public string KeyDelimiter { get; set; }
        public Func<string, IEnumerable<string>, bool> IsIndexAttribute { get; set; }
    }


    /// <summary>
    /// Parses xml stream to keys and values. 
    /// </summary>
    internal class XmlStreamToDictionaryParser
    {
        /// <summary>
        /// Loads the XML data from a stream.
        /// </summary>
        /// <param name="xmlStream">The stream to read.</param>
        /// <param name="options">The parser options.</param>
        public static IDictionary<string, string> Parse(Stream xmlStream, Action<XmlStreamToDictionaryParserOptions> options = null)
        {
            var parserOptions = new XmlStreamToDictionaryParserOptions 
            {
                KeyDelimiter = ConfigurationPath.KeyDelimiter, 
                IsIndexAttribute = (attribute, _) => string.Equals("Name", attribute, StringComparison.OrdinalIgnoreCase)
            };

            options?.Invoke(parserOptions);
            
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            var readerSettings = new XmlReaderSettings()
            {
                CloseInput = false, // caller will close the stream
                DtdProcessing = DtdProcessing.Prohibit,
                IgnoreComments = true,
                IgnoreWhitespace = true
            };

            xmlStream.Position = 0;

            // ReSharper disable once ConvertToUsingDeclaration
            using (var reader = XmlReader.Create(xmlStream, readerSettings))
            {
                var prefixStack = new Stack<string>();

                if (parserOptions.Parents != null)
                {
                    foreach (var parent in parserOptions.Parents)
                    {
                        prefixStack.Push(parent);
                    }
                }

                SkipUntilRootElement(reader);

                // We process the root element individually since it doesn't contribute to prefix 
                ProcessAttributes(reader, prefixStack, data, AddNamePrefix, parserOptions);
                ProcessAttributes(reader, prefixStack, data, AddAttributePair, parserOptions);

                var preNodeType = reader.NodeType;
                while (reader.Read())
                {
                    switch (reader.NodeType)
                    {
                        case XmlNodeType.Element:
                            prefixStack.Push(Capitalize(reader.LocalName));
                            ProcessAttributes(reader, prefixStack, data, AddNamePrefix, parserOptions);
                            ProcessAttributes(reader, prefixStack, data, AddAttributePair, parserOptions);

                            // If current element is self-closing
                            if (reader.IsEmptyElement)
                            {
                                prefixStack.Pop();
                            }
                            break;

                        case XmlNodeType.EndElement:
                            if (prefixStack.Any())
                            {
                                // If this EndElement node comes right after an Element node,
                                // it means there is no text/CDATA node in current element
                                if (preNodeType == XmlNodeType.Element)
                                {
                                    var key = Combine(prefixStack.Reverse(), parserOptions.KeyDelimiter);
                                    data[key] = string.Empty;
                                }

                                prefixStack.Pop();
                            }
                            break;

                        case XmlNodeType.CDATA:
                        case XmlNodeType.Text:
                            {
                                var key = Combine(prefixStack.Reverse(), parserOptions.KeyDelimiter);
                                if (data.ContainsKey(key))
                                {
                                    throw new FormatException($"A duplicate key '{key}' was found. {GetLineInfo(reader)}");
                                }

                                data[key] = reader.Value;
                                break;
                            }
                        case XmlNodeType.XmlDeclaration:
                        case XmlNodeType.ProcessingInstruction:
                        case XmlNodeType.Comment:
                        case XmlNodeType.Whitespace:
                            // Ignore certain types of nodes
                            break;

                        default:
                            throw new FormatException($"Unsupported node type '{reader.NodeType}' was found. {GetLineInfo(reader)}");
                    }
                    preNodeType = reader.NodeType;
                    // If this element is a self-closing element,
                    // we pretend that we just processed an EndElement node
                    // because a self-closing element contains an end within itself
                    if (preNodeType == XmlNodeType.Element &&
                        reader.IsEmptyElement)
                    {
                        preNodeType = XmlNodeType.EndElement;
                    }
                }
            }

            return data;
        }

        private static void SkipUntilRootElement(XmlReader reader)
        {
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.XmlDeclaration &&
                    reader.NodeType != XmlNodeType.ProcessingInstruction)
                {
                    break;
                }
            }
        }

        private static string GetLineInfo(XmlReader reader)
        {
            var lineInfo = reader as IXmlLineInfo;
            return lineInfo == null ? string.Empty : $"Line {lineInfo.LineNumber}, position {lineInfo.LinePosition}.";
        }

        private static void ProcessAttributes(XmlReader reader, Stack<string> prefixStack, IDictionary<string, string> data,
            Action<XmlReader, Stack<string>, IDictionary<string, string>, XmlWriter, XmlStreamToDictionaryParserOptions> act, XmlStreamToDictionaryParserOptions options, XmlWriter writer = null)
        {
            for (int i = 0; i < reader.AttributeCount; i++)
            {
                reader.MoveToAttribute(i);

                // If there is a namespace attached to current attribute
                if (!string.IsNullOrEmpty(reader.NamespaceURI))
                {
                    throw new FormatException($"XML namespaces are not supported. {GetLineInfo(reader)}");
                }

                act(reader, prefixStack, data, writer, options);
            }

            // Go back to the element containing the attributes we just processed
            reader.MoveToElement();
        }

        // The special attribute "Name" only contributes to prefix
        // This method adds a prefix if current node in reader represents a "Name" attribute
        private static void AddNamePrefix(XmlReader reader, Stack<string> prefixStack,
            IDictionary<string, string> data, XmlWriter writer, XmlStreamToDictionaryParserOptions options)
        {
            if (!options.IsIndexAttribute(reader.LocalName, prefixStack))
            {
                return;
            }

            if (prefixStack.Any())
            {
                prefixStack.Pop();
            }

            prefixStack.Push(reader.Value);
        }

        // Common attributes contribute to key-value pairs
        // This method adds a key-value pair if current node in reader represents a common attribute
        private static void AddAttributePair(XmlReader reader, Stack<string> prefixStack,
            IDictionary<string, string> data, XmlWriter writer, XmlStreamToDictionaryParserOptions options)
        {
            //if (options.IsIndexAttribute(reader.LocalName, prefixStack))
            //{
            //    return;
            //}

            prefixStack.Push(Capitalize(reader.LocalName));
            var key = Combine(prefixStack.Reverse(), options.KeyDelimiter);
            if (data.ContainsKey(key))
            {
                throw new FormatException($"A duplicate key '{key}' was found. {GetLineInfo(reader)}");
            }

            data[key] = reader.Value;

            prefixStack.Pop();
        }

        private static string Combine(IEnumerable<string> pathSegments, string keyDelimiter)
        {
            if (pathSegments == null)
            {
                throw new ArgumentNullException(nameof(pathSegments));
            }
            return string.Join(keyDelimiter, pathSegments);
        }

        private static string Capitalize(string s)
        {
            var a = s.ToCharArray();
            a[0] = char.ToUpper(a[0]);
            return new string(a);
        }
    }
}