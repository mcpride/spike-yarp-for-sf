using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.ServiceFabric.Configuration;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.ReverseProxy.ServiceFabric.Tests
{
    public class UnitTest1
    {
        private readonly ITestOutputHelper _output;

        public UnitTest1(ITestOutputHelper output)
        {
            _output = output;
        }
        
        [Fact]
        public void TransformXmlToIConfigurationTest()
        {
            var labels = GetLabels();
            Assert.NotEmpty(labels);
            var builder = new ConfigurationBuilder();
            builder.Add(new LabelsConfigurationProvider(labels));
            var cfg = builder.Build();
            var section = cfg.GetSection("MyApp:MyService");
            var service = LabelsBinder.Get<ServiceOptions>(section);
            Assert.NotEmpty(service.Endpoints);
        }
        private IDictionary<string, string> GetLabels()
        {
            var xml =
                @"<?xml version=""1.0"" encoding=""utf-8""?>" +
                @"<Service enableDynamicOverrides=""false"">" +
                @"  <Endpoints>" +
                @"    <Endpoint id=""WebStatelessEndpoint"" enable=""true"">" +
                @"      <Routes>" +
                @"        <Route routeId=""RouteApi"" order=""1"">" +
                @"          <Match path=""/WebStateless/api/{**catch-all}"" />" +
                @"        </Route>" +
                @"        <Route routeId=""RouteUi"" order=""1"">" +
                @"          <Match path=""/WebStateless/ui/{**catch-all}"" />" +
                @"        </Route>" +
                @"      </Routes>" +
                @"    </Endpoint>" +
                @"  </Endpoints>" +
                @"</Service>";
            using (var xStream = new MemoryStream(Encoding.ASCII.GetBytes(xml)))
            {
                var dictionary = XmlStreamToDictionaryParser.Parse(xStream, options =>
                {
                    options.KeyDelimiter = ".";
                    options.Parents = new List<string> { "MyApp", "MyService" };
                    options.IsIndexAttribute = (attribute, stack) =>
                    {
                        switch (stack.FirstOrDefault())
                        {
                            case "Endpoint": return (string.Equals(attribute, "Id", StringComparison.OrdinalIgnoreCase));
                            case "Route": return (string.Equals(attribute, "RouteId", StringComparison.OrdinalIgnoreCase));
                        }
                        return false;
                    };
                });
                foreach (var pair in dictionary)
                {
                    _output.WriteLine($"{pair.Key} = {pair.Value}");
                }

                return dictionary;
            }
        }
    }
}
