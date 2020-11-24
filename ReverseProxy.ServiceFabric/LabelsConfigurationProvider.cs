using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal class LabelsConfigurationProvider : ConfigurationProvider, IConfigurationSource
    {
        private readonly IDictionary<string, string> _labels;

        public LabelsConfigurationProvider(IDictionary<string, string> labels)
        {
            _labels = labels;
        }
        
        public override void Load()
        {
            var data = (IDictionary<string, string>)new Dictionary<string, string>();
            try
            {
                if (!_labels.Any()) return;
                foreach (var pair in _labels)
                {
                    var key = pair.Key.Replace(ConfigurationValues.KeyDelimiter, ConfigurationPath.KeyDelimiter[0]);
                    data.Add(key, pair.Value);
                }
            }
            finally
            {
                Data = data;
            }
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return this;
        }
    }
}
