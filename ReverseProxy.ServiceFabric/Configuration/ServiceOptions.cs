using System;
using System.Collections.Generic;
using System.Fabric.Query;

namespace Microsoft.ReverseProxy.ServiceFabric.Configuration
{
    public class ServiceOptions
    {
        public IList<ServiceEndpointOptions> Endpoints { get; set; }

        public string Id { get; set; }

        public ServiceKind Kind { get; set; }

        public string ManifestVersion { get; set; }

        public Uri Name { get; set; }

        public string TypeName { get; set; }
    }
}
