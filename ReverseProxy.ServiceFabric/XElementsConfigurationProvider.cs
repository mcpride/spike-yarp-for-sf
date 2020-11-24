using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal class XElementsConfigurationProvider : ConfigurationProvider
    {
        private readonly IEnumerable<XElement> _elements;

        public XElementsConfigurationProvider(IEnumerable<XElement> elements)
        {
            _elements = elements;
        }
        
        public override void Load()
        {
            LoadAsync().ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private async Task LoadAsync()
        {
            var data = (IDictionary<string, string>) new Dictionary<string, string>();
            try
            {
                if (!_elements.Any()) return;

                await using (var stream = new MemoryStream())
                {
                    await using (var sw = new StreamWriter(stream))
                    {
                        using (var writer = new XmlNoNamespaceWriter(sw, new XmlWriterSettings { CloseOutput = false }))
                        {
                            foreach (var element in _elements)
                            {
                                element.Save(writer);
                            }

                            writer.Flush();
                            await sw.FlushAsync();

                            data = XmlStreamToDictionaryParser.Parse(stream, options =>
                                    {
                                        options.IsIndexAttribute = (attribute, stack) =>
                                        {
                                            switch (stack.FirstOrDefault())
                                            {
                                                case "Endpoint": return (string.Equals(attribute, "Id", StringComparison.OrdinalIgnoreCase));
                                                case "Route": return (string.Equals(attribute, "RouteId", StringComparison.OrdinalIgnoreCase));
                                            }
                                            return false;
                                        };
                                    }
                                );
                        }
                    }
                }
            }
            finally
            {
                Data = data;
            }
        }
    }
}
