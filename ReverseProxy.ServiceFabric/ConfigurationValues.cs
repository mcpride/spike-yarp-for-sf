using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    public static class ConfigurationValues
    {
        public static string ExtensionName = "Proxy";
        public static string ServiceLabelPrefix = "Service";
        public static char KeyDelimiter = '.';
        public static string EndpointsLabelPrefix = $"{ServiceLabelPrefix}{KeyDelimiter}Endpoints"; //Must start with ServiceLabelPrefix
    }
}
