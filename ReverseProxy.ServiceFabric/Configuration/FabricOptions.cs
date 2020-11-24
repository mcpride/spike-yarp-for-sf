using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.ReverseProxy.ServiceFabric.Configuration
{
    public class FabricOptions
    {
        public IList<ApplicationOptions> Applications { get; set; }
    }
}
