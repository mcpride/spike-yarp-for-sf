using System;
using System.Collections.Generic;
using System.Fabric.Health;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.ReverseProxy.ServiceFabric.Tests
{
    internal class ServiceFabricCaller: ICachedServiceFabricCaller
    {
        private readonly IQueryClientWrapper _queryClientWrapper;
        private readonly IServiceManagementClientWrapper _serviceManagementClientWrapper;

        public ServiceFabricCaller(IQueryClientWrapper queryClientWrapper, IServiceManagementClientWrapper serviceManagementClientWrapper)
        {
            _queryClientWrapper = queryClientWrapper;
            _serviceManagementClientWrapper = serviceManagementClientWrapper;
        }
        
        public async Task<IEnumerable<ApplicationWrapper>> GetApplicationListAsync(CancellationToken cancellationToken)
        {
            return await _queryClientWrapper.GetApplicationListAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }

        public async Task<IEnumerable<ServiceWrapper>> GetServiceListAsync(Uri applicationName, CancellationToken cancellationToken)
        {
            return await _queryClientWrapper.GetServiceListAsync(applicationName, TimeSpan.FromSeconds(30), cancellationToken);
        }

        public async Task<IEnumerable<Guid>> GetPartitionListAsync(Uri serviceName, CancellationToken cancellation)
        {
            throw new NotImplementedException();
        }

        public async Task<IEnumerable<ReplicaWrapper>> GetReplicaListAsync(Guid partition, CancellationToken cancellation)
        {
            throw new NotImplementedException();
        }

        public async Task<string> GetServiceManifestAsync(string applicationTypeName, string applicationTypeVersion, string serviceManifestName,
            CancellationToken cancellationToken)
        {
            return await _serviceManagementClientWrapper.GetServiceManifestAsync(applicationTypeName,
                applicationTypeVersion, serviceManifestName, TimeSpan.FromSeconds(30), cancellationToken);
        }

        public async Task<string> GetServiceManifestName(string applicationTypeName, string applicationTypeVersion, string serviceTypeNameFilter,
            CancellationToken cancellationToken)
        {
            return await _queryClientWrapper.GetServiceManifestName(applicationTypeName, applicationTypeVersion, serviceTypeNameFilter, TimeSpan.FromSeconds(30), cancellationToken);
        }

        public async Task<IDictionary<string, string>> EnumeratePropertiesAsync(Uri parentName, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public void ReportHealth(HealthReport healthReport, HealthReportSendOptions sendOptions)
        {
            throw new NotImplementedException();
        }

        public void CleanUpExpired()
        {
            throw new NotImplementedException();
        }
    }
}
