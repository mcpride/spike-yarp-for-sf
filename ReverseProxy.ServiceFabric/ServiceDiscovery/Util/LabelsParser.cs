// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.ReverseProxy.Abstractions;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Helper class to parse configuration labels of the gateway into actual objects.
    /// </summary>
    // TODO: this is probably something that can be used in other integration modules apart from Service Fabric. Consider extracting to a general class.
    internal static class LabelsParser
    {
        // TODO: decide which labels are needed and which default table (and to what values)
        // Also probably move these defaults to the corresponding config entities.
        internal static readonly int DefaultCircuitbreakerMaxConcurrentRequests = 0;
        internal static readonly int DefaultCircuitbreakerMaxConcurrentRetries = 0;
        internal static readonly double DefaultQuotaAverage = 0;
        internal static readonly double DefaultQuotaBurst = 0;
        internal static readonly int DefaultPartitionCount = 0;
        internal static readonly string DefaultPartitionKeyExtractor = null;
        internal static readonly string DefaultPartitioningAlgorithm = "SHA256";
        internal static readonly int? DefaultRoutePriority = null;

        private static readonly Regex AllowedNamesRegex = new Regex("^[a-zA-Z0-9_-]+$");

        internal static TValue GetLabel<TValue>(IDictionary<string, string> labels, string key, TValue defaultValue)
        {
            if (!labels.TryGetValue(key, out var value))
            {
                return defaultValue;
            }
            else
            {
                try
                {
                    return (TValue) TypeDescriptor.GetConverter(typeof(TValue)).ConvertFromString(value);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is FormatException ||
                                           ex is NotSupportedException)
                {
                    throw new ConfigException(
                        $"Could not convert label {key}='{value}' to type {typeof(TValue).FullName}.", ex);
                }
            }
        }

        // TODO: optimize this method
        internal static List<ProxyRoute> BuildRoutes(Uri serviceName, string endpointName, IDictionary<string, string> labels)
        {
            var backendId = GetClusterId(serviceName, endpointName, labels);

            // Look for route IDs
            var routesLabelsPrefix = $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Routes{ConfigurationValues.KeyDelimiter}";
            var routesNames = new HashSet<string>();
            foreach (var kvp in labels)
            {
                if (kvp.Key.Length > routesLabelsPrefix.Length &&
                    kvp.Key.StartsWith(routesLabelsPrefix, StringComparison.Ordinal))
                {
                    var suffix = kvp.Key.Substring(routesLabelsPrefix.Length);
                    var routeNameLength = suffix.IndexOf(ConfigurationValues.KeyDelimiter);
                    if (routeNameLength == -1)
                    {
                        // No route name encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                        continue;
                    }

                    var routeName = suffix.Substring(0, routeNameLength);
                    if (!AllowedNamesRegex.IsMatch(routeName))
                    {
                        throw new ConfigException(
                            $"Invalid route name '{routeName}', should only contain alphanumerical characters, underscores or hyphens.");
                    }

                    routesNames.Add(routeName);
                }
            }

            // Build the routes
            var routes = new List<ProxyRoute>();
            foreach (var routeName in routesNames)
            {
                var thisRoutePrefix = $"{routesLabelsPrefix}{routeName}";
                var metadata = new Dictionary<string, string>();
                foreach (var kvp in labels)
                {
                    if (kvp.Key.StartsWith($"{thisRoutePrefix}.Metadata.", StringComparison.Ordinal))
                    {
                        metadata.Add(kvp.Key.Substring($"{thisRoutePrefix}{ConfigurationValues.KeyDelimiter}Metadata{ConfigurationValues.KeyDelimiter}".Length), kvp.Value);
                    }
                }

                labels.TryGetValue($"{thisRoutePrefix}{ConfigurationValues.KeyDelimiter}Match{ConfigurationValues.KeyDelimiter}Hosts", out var hosts);
                labels.TryGetValue($"{thisRoutePrefix}{ConfigurationValues.KeyDelimiter}Match{ConfigurationValues.KeyDelimiter}Path", out var path);

                var route = new ProxyRoute
                {
                    RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(routeName)}",
                    Match =
                    {
                        Hosts = SplitHosts(hosts),
                        Path = path
                    },
                    //TODO: mastolze
                    Order = GetLabel(labels, $"{thisRoutePrefix}{ConfigurationValues.KeyDelimiter}Order", DefaultRoutePriority),
                    ClusterId = backendId,
                    Metadata = metadata,
                };
                routes.Add(route);
            }

            return routes;
        }

        internal static Cluster BuildCluster(Uri serviceName, string endpointName, IDictionary<string, string> labels)
        {
            var clusterMetadata = new Dictionary<string, string>();
            var backendMetadataKeyPrefix = $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Metadata{ConfigurationValues.KeyDelimiter}";
            foreach (var item in labels)
            {
                if (item.Key.StartsWith(backendMetadataKeyPrefix, StringComparison.Ordinal))
                {
                    clusterMetadata[item.Key.Substring(backendMetadataKeyPrefix.Length)] = item.Value;
                }
            }

            var clusterId = GetClusterId(serviceName, endpointName, labels);

            var cluster = new Cluster
            {
                Id = clusterId,
                CircuitBreaker = new CircuitBreakerOptions
                {
                    MaxConcurrentRequests = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}CircuitBreaker{ConfigurationValues.KeyDelimiter}MaxConcurrentRequests",
                        DefaultCircuitbreakerMaxConcurrentRequests),
                    MaxConcurrentRetries = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}CircuitBreaker{ConfigurationValues.KeyDelimiter}MaxConcurrentRetries",
                        DefaultCircuitbreakerMaxConcurrentRequests),
                },
                Quota = new QuotaOptions
                {
                    Average = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Quota{ConfigurationValues.KeyDelimiter}Average", DefaultQuotaAverage),
                    Burst = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Quota{ConfigurationValues.KeyDelimiter}Burst", DefaultQuotaBurst),
                },
                Partitioning = new ClusterPartitioningOptions
                {
                    PartitionCount = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Partitioning{ConfigurationValues.KeyDelimiter}Count", DefaultPartitionCount),
                    PartitionKeyExtractor = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Partitioning{ConfigurationValues.KeyDelimiter}KeyExtractor",
                        DefaultPartitionKeyExtractor),
                    PartitioningAlgorithm = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Partitioning{ConfigurationValues.KeyDelimiter}Algorithm",
                        DefaultPartitioningAlgorithm),
                },
                LoadBalancing = new LoadBalancingOptions(), // TODO
                HealthCheck = new HealthCheckOptions
                {
                    Enabled = GetLabel(labels, $"{ConfigurationValues.EndpointsLabelPrefix}.{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Healthcheck{ConfigurationValues.KeyDelimiter}Enabled", false),
                    Interval = TimeSpan.FromSeconds(GetLabel<double>(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Healthcheck{ConfigurationValues.KeyDelimiter}Interval", 0)),
                    Timeout = TimeSpan.FromSeconds(GetLabel<double>(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Healthcheck{ConfigurationValues.KeyDelimiter}Timeout", 0)),
                    Port = GetLabel<int>(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}Healthcheck{ConfigurationValues.KeyDelimiter}Port", 0),
                    Path = GetLabel<string>(labels, $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}.Backend{ConfigurationValues.KeyDelimiter}Healthcheck{ConfigurationValues.KeyDelimiter}Path", null),
                },
                Metadata = clusterMetadata,
                HttpClient = new ProxyHttpClientOptions
                {
                    DangerousAcceptAnyServerCertificate = true
                }
            };
            return cluster;
        }

        private static string GetClusterId(Uri serviceName, string endpointName, IDictionary<string, string> labels)
        {
            if (!labels.TryGetValue($"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}BackendId", out var backendId) ||
                string.IsNullOrEmpty(backendId))
            {
                backendId = $"{serviceName}/{endpointName}";
            }

            return backendId;
        }

        private static IReadOnlyList<string> SplitHosts(string hosts)
        {
            return hosts?.Split(',').Select(h => h.Trim()).Where(h => h.Length > 0).ToList();
        }

        internal static IReadOnlyList<string> GetEndpointNames(IDictionary<string, string> labels)
        {
            var prefix = $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}";
            var names = new List<string>();
            foreach (var kvp in labels)
            {
                if (kvp.Key.Length > prefix.Length &&
                    kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    var suffix = kvp.Key.Substring(prefix.Length);
                    var nameLength = suffix.IndexOf(ConfigurationValues.KeyDelimiter);
                    if (nameLength == -1)
                    {
                        // No cluster name encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                        continue;
                    }

                    var name = suffix.Substring(0, nameLength);
                    
                    if (names.Contains(name))
                    {
                        continue;
                    }

                    if (!AllowedNamesRegex.IsMatch(name))
                    {
                        throw new ConfigException(
                            $"Invalid endpoint name '{name}', should only contain alphanumerical characters, underscores or hyphens.");
                    }

                    names.Add(name);
                }
            }
            return names;
        }
    }
}