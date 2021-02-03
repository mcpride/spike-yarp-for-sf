using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <inheritdoc />
    internal class ServiceExtensionConfigurationProvider : ConfigurationProvider
    {
        private ConfigurationReloadToken _delayedReloadToken = new ConfigurationReloadToken();
        private readonly ILogger<ServiceExtensionConfigurationProvider> _logger;
        private readonly ICachedServiceFabricCaller _serviceFabricCaller;
        private readonly TimeSpan _discoveryPeriod;
        private readonly CancellationToken _cancellationToken;

        public ServiceExtensionConfigurationProvider(
            ILogger<ServiceExtensionConfigurationProvider> logger,
            ICachedServiceFabricCaller serviceFabricCaller,
            TimeSpan discoveryPeriod,
            CancellationToken cancellationToken
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _serviceFabricCaller = serviceFabricCaller ?? throw new ArgumentNullException(nameof(serviceFabricCaller));
            _discoveryPeriod = discoveryPeriod;
            _cancellationToken = cancellationToken;

            if (ShouldReload())
            {
                ChangeToken.OnChange(
                    () => _delayedReloadToken,
                    () =>
                    {
                        Task.Delay(discoveryPeriod, _cancellationToken).ContinueWith(t => Load());
                    });
            }

        }

        private bool ShouldReload()
        {
            return _discoveryPeriod > TimeSpan.FromSeconds(1);
        }

        /// <inheritdoc />
        public override void Load()
        {
            ReloadLoadAsync(_cancellationToken).GetAwaiter().GetResult();
        }

        private async Task ReloadLoadAsync(CancellationToken cancellationToken)
        {
            try
            {
                var data = await LoadAsync(cancellationToken);
                if (!DataEquals(Data, data))
                {
                    Data = data;
                    OnReload(); //trigger public token for signaling changes
                }
            }
            finally
            {
                if (ShouldReload())
                {
                    TriggerDelayedReload();
                }
            }
        }

        private static bool DataEquals(IDictionary<string, string> data1, IDictionary<string, string> data2)
        {
            if (data1.Count != data2.Count) return false;
            foreach (var pair in data1)
            {
                if (!data2.TryGetValue(pair.Key, out var value)) return false;
                if (value != pair.Value)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Triggers the reload change token and creates a new one.
        /// </summary>
        private void TriggerDelayedReload()
        {
            var previousToken = Interlocked.Exchange(ref _delayedReloadToken, new ConfigurationReloadToken());
            previousToken.OnReload(); //trigger private token for delayed reload
        }


        /// <summary>
        /// Loads (or reloads) the data for this provider.
        /// </summary>
        public async Task<IDictionary<string, string>> LoadAsync(CancellationToken cancellationToken)
        {
            var data = new Dictionary<string, string>();
            
            IEnumerable<ApplicationWrapper> applications;
            try
            {
                applications = await _serviceFabricCaller.GetApplicationListAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // TODO: davidni: not fatal?
            {
                // The serviceFabricCaller does their best effort to use LKG information, nothing we can do at this point
                _logger.LogError(ex, "Could not get applications list from Service Fabric, continuing with zero applications.");
                applications = Enumerable.Empty<ApplicationWrapper>();
            }

            foreach (var application in applications)
            {
                await LoadApplicationDataAsync(data, application, cancellationToken);
            }

            return data;
        }

        private async Task LoadApplicationDataAsync(Dictionary<string, string> data, ApplicationWrapper application, CancellationToken cancellationToken)
        {
            IEnumerable<ServiceWrapper> services;

            try
            {
                services = await _serviceFabricCaller.GetServiceListAsync(application.ApplicationName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // TODO: davidni: not fatal?
            {
                _logger.LogError(ex,
                    $"Could not get service list for application {application.ApplicationName}, skipping application.");
                return;
            }

            var appId = application.ApplicationName.ToString().Replace("fabric:/", string.Empty);
            var appPrefix =
                $"Fabric{ConfigurationPath.KeyDelimiter}Applications{ConfigurationPath.KeyDelimiter}{appId}{ConfigurationPath.KeyDelimiter}";
            data[$"{appPrefix}Id"] = appId;
            data[$"{appPrefix}Name"] = application.ApplicationName.ToString();
            data[$"{appPrefix}TypeName"] = application.ApplicationTypeName;
            data[$"{appPrefix}TypeVersion"] = application.ApplicationTypeVersion;
            foreach (var parameter in application.ApplicationParameters)
            {
                var paramPrefix =
                    $"{appPrefix}Parameters{ConfigurationPath.KeyDelimiter}{parameter.Key}{ConfigurationPath.KeyDelimiter}";
                data[$"{paramPrefix}Name"] = parameter.Key;
                data[$"{paramPrefix}Value"] = parameter.Value;
            }

            foreach (var service in services)
            {
                await LoadServiceDataAsync(data, application, appPrefix, service, cancellationToken);
            }
        }

        private async Task LoadServiceDataAsync(Dictionary<string, string> data, ApplicationWrapper application, string appPrefix,
            ServiceWrapper service, CancellationToken cancellationToken)
        {
            string serviceManifestName;
            try
            {
                serviceManifestName = await _serviceFabricCaller.GetServiceManifestName(application.ApplicationTypeName,
                    application.ApplicationTypeVersion, service.ServiceTypeName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // TODO: davidni: not fatal?
            {
                throw new ServiceFabricIntegrationException(
                    $"Failed to get service manifest name for service type {service.ServiceTypeName} of application type {application.ApplicationTypeName} {application.ApplicationTypeVersion} from Service Fabric: {ex}.");
            }

            if (serviceManifestName == null)
            {
                throw new ServiceFabricIntegrationException(
                    $"No service manifest name was found for service type {service.ServiceTypeName} of application type {application.ApplicationTypeName} {application.ApplicationTypeVersion}.");
            }

            string rawServiceManifest;
            try
            {
                rawServiceManifest = await _serviceFabricCaller.GetServiceManifestAsync(application.ApplicationTypeName,
                    application.ApplicationTypeVersion, serviceManifestName, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) // TODO: davidni: not fatal?
            {
                throw new ServiceFabricIntegrationException(
                    $"Failed to get service manifest {serviceManifestName} of service type {service.ServiceTypeName} of application type {application.ApplicationTypeName} {application.ApplicationTypeVersion} from Service Fabric: {ex}.");
            }

            if (rawServiceManifest == null)
            {
                throw new ServiceFabricIntegrationException(
                    $"No service manifest named '{serviceManifestName}' was found for service type {service.ServiceTypeName} of application type {application.ApplicationTypeName} {application.ApplicationTypeVersion}.");
            }

            using (var reader = XmlReader.Create(new StringReader(rawServiceManifest), XmlReaderHelper.CreateSafeXmlSetting()))
            {
                XDocument parsedManifest;
                try
                {
                    parsedManifest = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
                }
                catch (System.Xml.XmlException ex)
                {
                    // TODO: we don't know if the service wants to use the gateway yet, so not sure if this classifies as config error (considering it will escalate into a bad health report)
                    throw new ConfigException("Failed to parse service manifest XML.", ex);
                }

                var elements = parsedManifest
                    .Elements(XmlReaderHelper.XNSServiceManifest + "ServiceManifest")
                    .Elements(XmlReaderHelper.XNSServiceManifest + "ServiceTypes")
                    .Elements().Where(s => (string) s.Attribute("ServiceTypeName") == service.ServiceTypeName)
                    .Elements(XmlReaderHelper.XNSServiceManifest + "Extensions")
                    .Elements(XmlReaderHelper.XNSServiceManifest + "Extension").Where(s =>
                        (string) s.Attribute("Name") == ConfigurationValues.ExtensionName)
                    .Elements(XmlReaderHelper.XNSFabricNoSchema + "Service");

                if (!elements.Any()) return;

                var serviceId = service.ServiceName.ToString().Replace($"{application.ApplicationName}/", string.Empty);
                var servicePrefix =
                    $"{appPrefix}Services{ConfigurationPath.KeyDelimiter}{serviceId}{ConfigurationPath.KeyDelimiter}";
                data[$"{servicePrefix}Id"] = serviceId;
                data[$"{servicePrefix}Name"] = service.ServiceName.ToString();
                data[$"{servicePrefix}TypeName"] = service.ServiceTypeName;
                data[$"{servicePrefix}Kind"] = service.ServiceKind.ToString();
                data[$"{servicePrefix}ManifestVersion"] = service.ServiceManifestVersion;

                await using (var stream = new MemoryStream())
                {
                    await using (var sw = new StreamWriter(stream))
                    {
                        using (var writer = new XmlNoNamespaceWriter(sw, new XmlWriterSettings {CloseOutput = false}))
                        {
                            foreach (var element in elements)
                            {
                                element.Save(writer);
                            }

                            writer.Flush();
                            await sw.FlushAsync();

                            var sections = XmlStreamToDictionaryParser.Parse(stream, (options) =>
                            {
                                options.KeyDelimiter = ConfigurationPath.KeyDelimiter;
                                options.Parents = new List<string>(servicePrefix.Split(ConfigurationPath.KeyDelimiter,
                                    StringSplitOptions.RemoveEmptyEntries));
                                options.IsIndexAttribute = (attribute, stack) =>
                                {
                                    switch (stack.FirstOrDefault())
                                    {
                                        case "Endpoint":
                                            return (string.Equals(attribute, "Id", StringComparison.OrdinalIgnoreCase));
                                        case "Route":
                                            return (string.Equals(attribute, "Id", StringComparison.OrdinalIgnoreCase));
                                    }

                                    return false;
                                };
                            });
                            foreach (var section in sections)
                            {
                                data[section.Key] = section.Value;
                            }
                        }
                    }
                }
            }
        }
    }
}
