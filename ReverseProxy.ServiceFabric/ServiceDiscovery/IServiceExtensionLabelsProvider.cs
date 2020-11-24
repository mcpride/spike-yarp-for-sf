// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Provides the Island Gateway's configuration labels as gathered from Service Fabric.
    /// It looks for the labels in the ServiceManifest.xml and overrides in the service's properties.
    /// </summary>
    /// <remarks>
    /// The key-value labels to configure the proxy are first read from the Proxy extension
    /// in the "Extensions" section of a service in the ServiceManifest.xml. Example:
    /// <![CDATA[
    /// <StatelessServiceType ServiceTypeName="WebStatelessServiceType">
    ///   <Extensions>
    ///     <Extension Name="Proxy">
    ///       <Service xmlns="http://schemas.microsoft.com/2015/03/fabact-no-schema" EnableDynamicOverrides="false">
    ///         <Endpoints>
    ///           <Endpoint id="WebStatelessEndpoint" Enable="true">
    ///             <Routes>
    ///               <Route id="RouteApi" Order="1">
    ///                 <Match Path="/WebStateless/{**catch-all}" />
    ///               </Route>
    ///             </Routes>
    ///           </Endpoint>
    ///         </Endpoints>
    ///       </Service>
    ///     </Extension>
    ///   </Extensions>
    /// </StatelessServiceType>
    /// ]]>
    /// Once gathered, the labels are overrode with properties of the service. See
    /// <seealso href="https://docs.microsoft.com/en-us/rest/api/servicefabric/sfclient-index-property-management"/>
    /// for more information about properties.
    /// Refer to the Island Gateway documentation for further details about labels and their format.
    /// </remarks>
    internal interface IServiceExtensionLabelsProvider
    {
        /// <summary>
        /// Gets the labels representing the current Island Gateway configuration for the specified service.
        /// </summary>
        /// <exception cref="ServiceFabricIntegrationException">Failed to get or parse the required information from service fabric.</exception>
        Task<IDictionary<string, string>> GetExtensionLabelsAsync(ApplicationWrapper application, ServiceWrapper service, CancellationToken cancellationToken);
    }
}
