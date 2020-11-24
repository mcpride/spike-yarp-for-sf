using System;
using System.IO;
using System.Threading.Tasks;
using System.Xml;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Modified XML writer that writes no namespaces
    /// </summary>
    internal class XmlNoNamespaceWriter : XmlTextWriter
    {
        private bool _skipAttribute;

        public override XmlWriterSettings Settings { get; }

        public XmlNoNamespaceWriter(TextWriter writer, XmlWriterSettings settings)
            : base(writer)
        {
            Formatting = Formatting.None;
            Settings = settings;
        }

        public override void WriteStartElement(string prefix, string localName, string ns)
        {
            base.WriteStartElement(null!, localName, null!);
        }

        public override async Task WriteStartElementAsync(string prefix, string localName, string ns)
        {
            await base.WriteStartElementAsync(null, localName, null);
        }

        public override void WriteStartAttribute(string prefix, string localName, string ns)
        {
            //If the prefix or local name are "xmlns", don't write it.
            if ((string.Compare(prefix, "xmlns", StringComparison.OrdinalIgnoreCase) == 0
                 || string.Compare(localName, "xmlns", StringComparison.OrdinalIgnoreCase) == 0))
            {
                _skipAttribute = true;
            }
            else
            {
                base.WriteStartAttribute(null!, localName, null!);
            }
        }

        protected override async Task WriteStartAttributeAsync(string prefix, string localName, string ns)
        {
            //If the prefix or local name are "xmlns", don't write it.
            if ((string.Compare(prefix, "xmlns", StringComparison.OrdinalIgnoreCase) == 0
                 || string.Compare(localName, "xmlns", StringComparison.OrdinalIgnoreCase) == 0))
            {
                _skipAttribute = true;
            }
            else
            {
                await base.WriteStartAttributeAsync(null, localName, null);
            }
        }

        public override void WriteString(string text)
        {
            //If we are writing an attribute, the text for the xmlns
            //or xmlns:prefix declaration would occur here.  Skip
            //it if this is the case.
            if (!_skipAttribute)
            {
                base.WriteString(text);
            }
        }

        public override async Task WriteStringAsync(string text)
        {
            //If we are writing an attribute, the text for the xmlns
            //or xmlns:prefix declaration would occur here.  Skip
            //it if this is the case.
            if (!_skipAttribute)
            {
                await base.WriteStringAsync(text);
            }
        }

        public override void WriteEndAttribute()
        {
            //If we skipped the WriteStartAttribute call, we have to
            //skip the WriteEndAttribute call as well or else the XmlWriter
            //will have an invalid state.
            if (!_skipAttribute)
            {
                base.WriteEndAttribute();
            }
            //reset the boolean for the next attribute.
            _skipAttribute = false;
        }

        protected override async Task WriteEndAttributeAsync()
        {
            //If we skipped the WriteStartAttribute call, we have to
            //skip the WriteEndAttribute call as well or else the XmlWriter
            //will have an invalid state.
            if (!_skipAttribute)
            {
                await base.WriteEndAttributeAsync();
            }
            //reset the boolean for the next attribute.
            _skipAttribute = false;
        }

        public override void WriteQualifiedName(string localName, string ns)
        {
            //Always write the qualified name using only the local name.
            base.WriteQualifiedName(localName, null);
        }

        public override async Task WriteQualifiedNameAsync(string localName, string ns)
        {
            //Always write the qualified name using only the local name.
            await base.WriteQualifiedNameAsync(localName, null);
        }
    }
}