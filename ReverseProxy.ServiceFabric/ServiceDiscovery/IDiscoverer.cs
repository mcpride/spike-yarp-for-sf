// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ReverseProxy.Abstractions;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Discovers Service Fabric services and builds the corresponding
    /// <see cref="ProxyRoute"/> and <see cref="Cluster"/> instances that represent them.
    /// </summary>
    internal interface IDiscoverer
    {
        /// <summary>
        /// Execute the discovery and update entities.
        /// </summary>
        Task<(IReadOnlyList<ProxyRoute> Routes, IReadOnlyList<Cluster> Clusters)> DiscoverAsync(CancellationToken cancellation);
    }
}
