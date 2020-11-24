using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.ReverseProxy.ServiceFabric.Configuration
{
    public class ApplicationOptions
    {
        public string Id { get; set; }

        public Uri Name { get; set; }

        public IList<ParameterOptions> Parameters { get; set; }

        public IList<ServiceOptions> Services { get; set; }

        public string TypeName { get; set; }

        public string TypeVersion { get; set; }
    }
}
