using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.ReverseProxy.ServiceFabric.Configuration;
using Xunit;
using System;
using System.Threading;

namespace Microsoft.ReverseProxy.ServiceFabric.Tests
{
    public class ServiceExtensionConfigurationProviderTests
    {
        [Fact]
        public void TestGetData()
        {
            var loggerFactory = LoggerFactory.Create(_ =>{});
            var qLogger = loggerFactory.CreateLogger<QueryClientWrapper>();
            var queryClientWrapper = new QueryClientWrapper(qLogger);
            var sf = new ServiceFabricCaller(queryClientWrapper, new ServiceManagementClientWrapper());

            var source = new ServiceExtensionConfigurationSource(loggerFactory, sf, TimeSpan.Zero, CancellationToken.None);
            var builder = new ConfigurationBuilder();
            builder.Add(source);
            var configuration = builder.Build();
            var section = configuration.GetSection("Fabric");
            var fab = section.Get<FabricOptions>();

            Assert.NotEmpty(fab.Applications);
        }
    }
}
