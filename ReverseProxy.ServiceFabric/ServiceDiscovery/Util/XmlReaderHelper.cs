using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal class XmlReaderHelper
    {
        public static readonly XNamespace XNSServiceManifest = "http://schemas.microsoft.com/2011/01/fabric";
        public static readonly XNamespace XNSFabricNoSchema = "http://schemas.microsoft.com/2015/03/fabact-no-schema";

        /// <summary>
        /// This class creates XmlReaderSettings providing Safe Xml parsing in the senses below:
        ///     1. DTD processing is disabled to prevent Xml Bomb.
        ///     2. XmlResolver is disabled to prevent Schema/External DTD resolution.
        ///     3. Comments/processing instructions are not allowed.
        /// </summary>
        public static XmlReaderSettings CreateSafeXmlSetting()
        {
            return new XmlReaderSettings
            {
                Async = true,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                IgnoreWhitespace = true,
                XmlResolver = null,
                DtdProcessing = DtdProcessing.Prohibit,
            };
        }

}
}
