// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Fabric.Health;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.ReverseProxy.ServiceFabric.Utilities;
using Microsoft.ReverseProxy.Utilities;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal sealed class CachedServiceFabricCaller : ICachedServiceFabricCaller
    {
        public static readonly TimeSpan CacheExpirationOffset = TimeSpan.FromMinutes(30);
        private static readonly TimeSpan _defaultTimeout = TimeSpan.FromSeconds(60);

        private readonly ILogger<CachedServiceFabricCaller> _logger;
        private readonly IClock _clock;
        private readonly IQueryClientWrapper _queryClientWrapper;
        private readonly IServiceManagementClientWrapper _serviceManagementClientWrapper;
        private readonly IPropertyManagementClientWrapper _propertyManagementClientWrapper;
        private readonly IHealthClientWrapper _healthClientWrapper;

        private readonly Cache<IEnumerable<ApplicationWrapper>> _applicationListCache;
        private readonly Cache<IEnumerable<ServiceWrapper>> _serviceListCache;
        private readonly Cache<IEnumerable<Guid>> _partitionListCache;
        private readonly Cache<IEnumerable<ReplicaWrapper>> _replicaListCache;
        private readonly Cache<string> _serviceManifestCache;
        private readonly Cache<string> _serviceManifestNameCache;
        private readonly Cache<IDictionary<string, string>> _propertiesCache;

        public CachedServiceFabricCaller(
            ILogger<CachedServiceFabricCaller> logger,
            IClock clock,
            IQueryClientWrapper queryClientWrapper,
            IServiceManagementClientWrapper serviceManagementClientWrapper,
            IPropertyManagementClientWrapper propertyManagementClientWrapper,
            IHealthClientWrapper healthClientWrapper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _queryClientWrapper = queryClientWrapper ?? throw new ArgumentNullException(nameof(queryClientWrapper));
            _serviceManagementClientWrapper = serviceManagementClientWrapper ?? throw new ArgumentNullException(nameof(serviceManagementClientWrapper));
            _propertyManagementClientWrapper = propertyManagementClientWrapper ?? throw new ArgumentNullException(nameof(propertyManagementClientWrapper));
            _healthClientWrapper = healthClientWrapper ?? throw new ArgumentNullException(nameof(healthClientWrapper));

            _applicationListCache = new Cache<IEnumerable<ApplicationWrapper>>(_clock, CacheExpirationOffset);
            _serviceListCache = new Cache<IEnumerable<ServiceWrapper>>(_clock, CacheExpirationOffset);
            _partitionListCache = new Cache<IEnumerable<Guid>>(_clock, CacheExpirationOffset);
            _replicaListCache = new Cache<IEnumerable<ReplicaWrapper>>(_clock, CacheExpirationOffset);
            _serviceManifestCache = new Cache<string>(_clock, CacheExpirationOffset);
            _serviceManifestNameCache = new Cache<string>(_clock, CacheExpirationOffset);
            _propertiesCache = new Cache<IDictionary<string, string>>(_clock, CacheExpirationOffset);
        }

        public async Task<IEnumerable<ApplicationWrapper>> GetApplicationListAsync(CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetApplicationList",
                func: () => _queryClientWrapper.GetApplicationListAsync(timeout: _defaultTimeout, cancellationToken: cancellation),
                cache: _applicationListCache,
                key: string.Empty,
                cancellation);
        }
        public async Task<IEnumerable<ServiceWrapper>> GetServiceListAsync(Uri applicationName, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetServiceList",
                func: () => _queryClientWrapper.GetServiceListAsync(applicationName: applicationName, timeout: _defaultTimeout, cancellation),
                cache: _serviceListCache,
                key: applicationName.ToString(),
                cancellation);
        }

        public async Task<IEnumerable<Guid>> GetPartitionListAsync(Uri serviceName, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetPartitionList",
                func: () => _queryClientWrapper.GetPartitionListAsync(serviceName: serviceName, timeout: _defaultTimeout, cancellation),
                cache: _partitionListCache,
                key: serviceName.ToString(),
                cancellation);
        }

        public async Task<IEnumerable<ReplicaWrapper>> GetReplicaListAsync(Guid partition, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetReplicaList",
                func: () => _queryClientWrapper.GetReplicaListAsync(partitionId: partition, timeout: _defaultTimeout, cancellation),
                cache: _replicaListCache,
                key: partition.ToString(),
                cancellation);
        }

        public async Task<string> GetServiceManifestAsync(string applicationTypeName, string applicationTypeVersion, string serviceManifestName, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetServiceManifest",
                func: () => _serviceManagementClientWrapper.GetServiceManifestAsync(
                    applicationTypeName: applicationTypeName,
                    applicationTypeVersion: applicationTypeVersion,
                    serviceManifestName: serviceManifestName,
                    timeout: _defaultTimeout,
                    cancellation),
                cache: _serviceManifestCache,
                key: $"{Uri.EscapeDataString(applicationTypeName)}:{Uri.EscapeDataString(applicationTypeVersion)}:{Uri.EscapeDataString(serviceManifestName)}",
                cancellation);
        }

        public async Task<string> GetServiceManifestName(string applicationTypeName, string applicationTypeVersion, string serviceTypeNameFilter, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.GetServiceManifestName",
                func: () => _queryClientWrapper.GetServiceManifestName(
                    applicationTypeName: applicationTypeName,
                    applicationTypeVersion: applicationTypeVersion,
                    serviceTypeNameFilter: serviceTypeNameFilter,
                    timeout: _defaultTimeout,
                    cancellationToken: cancellation),
                cache: _serviceManifestNameCache,
                key: $"{Uri.EscapeDataString(applicationTypeName)}:{Uri.EscapeDataString(applicationTypeVersion)}:{Uri.EscapeDataString(serviceTypeNameFilter)}",
                cancellation);
        }

        public async Task<IDictionary<string, string>> EnumeratePropertiesAsync(Uri parentName, CancellationToken cancellation)
        {
            return await TryWithCacheFallbackAsync(
                operationName: $"{ConfigurationValues.ExtensionName}.ServiceFabric.EnumerateProperties",
                func: () => _propertyManagementClientWrapper.EnumeratePropertiesAsync(
                    parentName: parentName,
                    timeout: _defaultTimeout,
                    cancellationToken: cancellation),
                cache: _propertiesCache,
                key: parentName.ToString(),
                cancellation);
        }

        public void ReportHealth(HealthReport healthReport, HealthReportSendOptions sendOptions)
        {
            //_operationLogger.Execute(
            //    $"{ConfigurationValues.ExtensionName}.ServiceFabric.ReportHealth",
            //    () =>
            //    {
            //        var operationContext = _operationLogger.Context;
            //        operationContext?.SetProperty("kind", healthReport.Kind.ToString());
            //        operationContext?.SetProperty("healthState", healthReport.HealthInformation.HealthState.ToString());
            //        switch (healthReport)
            //        {
            //            case ServiceHealthReport service:
            //                operationContext?.SetProperty("serviceName", service.ServiceName.ToString());
            //                break;
            //            default:
            //                operationContext?.SetProperty("type", healthReport.GetType().FullName);
            //                break;
            //        }

            //        _healthClientWrapper.ReportHealth(healthReport, sendOptions);
            //    });
            Uri serviceName = null;
            if (healthReport is ServiceHealthReport service)
            {
                serviceName = service.ServiceName;
            }
            _logger.LogDebug($"Reporting health, kind='{healthReport.Kind}', healthState='{healthReport.HealthInformation.HealthState}', type='{healthReport.GetType().FullName}', serviceName='{serviceName}'");

            _healthClientWrapper.ReportHealth(healthReport, sendOptions);
        }

        public void CleanUpExpired()
        {
            _applicationListCache.Cleanup();
            _serviceListCache.Cleanup();
            _partitionListCache.Cleanup();
            _replicaListCache.Cleanup();
            _serviceManifestCache.Cleanup();
            _serviceManifestNameCache.Cleanup();
            _propertiesCache.Cleanup();
        }

        private async Task<T> TryWithCacheFallbackAsync<T>(string operationName, Func<Task<T>> func, Cache<T> cache, string key, CancellationToken cancellation)
        {
            _logger.LogDebug($"Starting operation '{operationName}'.Cache, key='{key}'");
            
            var outcome = "UnhandledException";
            try
            {
                _logger.LogDebug($"Starting inner operation '{operationName}', key='{key}'");
                var value = await func();
                cache.Set(key, value);
                outcome = "Success";
                return value;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                outcome = "Canceled";
                throw;
            }
            catch (Exception)
            {
                if (cache.TryGetValue(key, out var value))
                {
                    outcome = "CacheFallback";
                    return value;
                }
                else
                {
                    outcome = "Error";
                    throw;
                }
            }
            finally
            {
                _logger.LogInformation($"Operation '{operationName}'.Cache completed with key='{key}', outcome='{outcome}'");
            }
        }
    }
}
