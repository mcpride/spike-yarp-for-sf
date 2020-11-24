using Microsoft.Extensions.Configuration;
using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    internal class ServiceExtensionConfigurationSource : IConfigurationSource
    {
        private readonly ILoggerFactory _loggerFactory;
        private readonly IServiceFabricCaller _serviceFabricCaller;
        private readonly TimeSpan _discoveryPeriod;
        private readonly CancellationToken _cancellationToken;

        public ServiceExtensionConfigurationSource(
            ILoggerFactory loggerFactory,
            IServiceFabricCaller serviceFabricCaller,
            TimeSpan discoveryPeriod,
            CancellationToken cancellationToken)
        {
            _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
            _serviceFabricCaller = serviceFabricCaller ?? throw new ArgumentNullException(nameof(serviceFabricCaller));
            _discoveryPeriod = discoveryPeriod;
            _cancellationToken = cancellationToken;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new ServiceExtensionConfigurationProvider(
                _loggerFactory.CreateLogger<ServiceExtensionConfigurationProvider>(), 
                _serviceFabricCaller, 
                _discoveryPeriod, 
                _cancellationToken);
        }
    }
}
