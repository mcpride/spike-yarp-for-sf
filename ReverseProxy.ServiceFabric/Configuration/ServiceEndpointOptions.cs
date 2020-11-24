using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.ReverseProxy.Abstractions;

namespace Microsoft.ReverseProxy.ServiceFabric.Configuration
{
    public class ServiceEndpointOptions
    {
        public string Id { get; set; }
        public IList<ProxyRoute> Routes { get; set; }
    }
}
