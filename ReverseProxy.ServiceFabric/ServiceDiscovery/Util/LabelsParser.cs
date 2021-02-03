// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.ReverseProxy.Abstractions;
using Microsoft.Extensions.Primitives;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Helper class to parse configuration labels of the gateway into actual objects.
    /// </summary>
    // TODO: this is probably something that can be used in other integration modules apart from Service Fabric. Consider extracting to a general class.
    internal static class LabelsParser
    {
        private static readonly Regex _allowedRouteNamesRegex = new Regex("^[a-zA-Z0-9_-]+$");

        /// <summary>
        /// Requires all header match names to follow the .[0]. pattern to simulate indexing in an array
        /// </summary>
        private static readonly Regex _allowedHeaderNamesRegex = new Regex(@"^\[\d\d*\]$");


        /// Requires all transform names to follow the .[0]. pattern to simulate indexing in an array
        /// </summary>
        private static readonly Regex _allowedTransformNamesRegex = new Regex(@"^\[\d\d*\]$");

        internal static TValue GetLabel<TValue>(IDictionary<string, string> labels, string key, TValue defaultValue)
        {
            if (!labels.TryGetValue(key, out var value))
            {
                return defaultValue;
            }
            else
            {
                return ConvertLabelValue<TValue>(key, value);
            }
        }

        private static TValue ConvertLabelValue<TValue>(string key, string value)
        {
            try
            {
                return (TValue)TypeDescriptor.GetConverter(typeof(TValue)).ConvertFromString(value);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is NotSupportedException)
            {
                throw new ConfigException($"Could not convert label {key}='{value}' to type {typeof(TValue).FullName}.", ex);
            }
        }

        // TODO: optimize this method
        internal static List<ProxyRoute> BuildRoutes(Uri serviceName, string endpointName, IDictionary<string, string> labels)
        {
            var backendId = GetClusterId(serviceName, endpointName, labels);

            // Look for route IDs
            var routesLabelsPrefix = $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Routes{ConfigurationValues.KeyDelimiter}";
            
            var routesNames = new Dictionary<StringSegment, string>();
            foreach (var kvp in labels)
            {
                if (kvp.Key.Length > routesLabelsPrefix.Length &&
                    kvp.Key.StartsWith(routesLabelsPrefix, StringComparison.Ordinal))
                {
                    var suffix = new StringSegment(kvp.Key).Subsegment(routesLabelsPrefix.Length);
                    var routeNameLength = suffix.IndexOf(ConfigurationValues.KeyDelimiter);
                    if (routeNameLength == -1)
                    {
                        // No route name encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                        continue;
                    }

                    var routeNameSegment = suffix.Subsegment(0, routeNameLength + 1);
                    if (routesNames.ContainsKey(routeNameSegment))
                    {
                        continue;
                    }

                    var routeName = routeNameSegment.Subsegment(0, routeNameSegment.Length - 1).ToString();
                    if (!_allowedRouteNamesRegex.IsMatch(routeName))
                    {
                        throw new ConfigException(
                            $"Invalid route name '{routeName}', should only contain alphanumerical characters, underscores or hyphens.");
                    }

                    routesNames.Add(routeNameSegment, routeName);
                }
            }

            // Build the routes
            var routes = new List<ProxyRoute>();
            foreach (var routeNamePair in routesNames)
            {
                string hosts = null;
                string path = null;
                int? order = null;
                var metadata = new Dictionary<string, string>();
                var headerMatches = new Dictionary<string, RouteHeader>();
                var transforms = new Dictionary<string, IDictionary<string, string>>();
                foreach (var kvp in labels)
                {
                    if(!kvp.Key.StartsWith(routesLabelsPrefix, StringComparison.Ordinal))
                    {
                        continue;
                    }
                    
                    var routeLabelKey = kvp.Key.AsSpan().Slice(routesLabelsPrefix.Length);

                    if(routeLabelKey.Length < routeNamePair.Key.Length || !routeLabelKey.StartsWith(routeNamePair.Key, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    routeLabelKey = routeLabelKey.Slice(routeNamePair.Key.Length);

                    if (ContainsKey("Metadata.", routeLabelKey, out var keyRemainder))
                    {
                        metadata.Add(keyRemainder.ToString(), kvp.Value);
                    }
                    else if (ContainsKey("MatchHeaders.", routeLabelKey, out keyRemainder))
                    {
                        var headerIndexLength = keyRemainder.IndexOf('.');
                        if (headerIndexLength == -1)
                        {
                            // No header encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                            continue;
                        }
                        var headerIndex = keyRemainder.Slice(0, headerIndexLength).ToString();
                        if (!_allowedHeaderNamesRegex.IsMatch(headerIndex))
                        {
                            throw new ConfigException($"Invalid header matching index '{headerIndex}', should only contain alphanumerical characters, underscores or hyphens.");
                        }
                        if (!headerMatches.ContainsKey(headerIndex))
                        {
                            headerMatches.Add(headerIndex, new RouteHeader());
                        }

                        var propertyName = keyRemainder.Slice(headerIndexLength + 1);
                        if (propertyName.Equals("Name", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].Name = kvp.Value;
                        }
                        else if (propertyName.Equals("Values", StringComparison.Ordinal))
                        {
#if NET5_0
                            headerMatches[headerIndex].Values = kvp.Value.Split(',', StringSplitOptions.TrimEntries);
#elif NETCOREAPP3_1
                            headerMatches[headerIndex].Values = kvp.Value.Split(',').Select(val => val.Trim()).ToList();
#else
#error A target framework was added to the project and needs to be added to this condition.
#endif
                        }
                        else if (propertyName.Equals("IsCaseSensitive", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].IsCaseSensitive = bool.Parse(kvp.Value);
                        }
                        else if (propertyName.Equals("Mode", StringComparison.Ordinal))
                        {
                            headerMatches[headerIndex].Mode = Enum.Parse<HeaderMatchMode>(kvp.Value);
                        }
                        else
                        {
                            throw new ConfigException($"Invalid header matching property '{propertyName.ToString()}', only valid values are Name, Values, IsCaseSensitive and Mode.");
                        }
                    }
                    else if (ContainsKey("Transforms.", routeLabelKey, out keyRemainder))
                    {
                        var transformNameLength = keyRemainder.IndexOf('.');
                        if (transformNameLength == -1)
                        {
                            // No transform index encoded, the key is not valid. Throwing would suggest we actually check for all invalid keys, so just ignore.
                            continue;
                        }
                        var transformName = keyRemainder.Slice(0, transformNameLength).ToString();
                        if (!_allowedTransformNamesRegex.IsMatch(transformName))
                        {
                            throw new ConfigException($"Invalid transform index '{transformName}', should be transform index wrapped in square brackets.");
                        }
                        if (!transforms.ContainsKey(transformName))
                        {
                            transforms.Add(transformName, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
                        }
                        var propertyName = keyRemainder.Slice(transformNameLength + 1).ToString();
                        if (!transforms[transformName].ContainsKey(propertyName))
                        {
                            transforms[transformName].Add(propertyName, kvp.Value);
                        }
                        else
                        {
                            throw new ConfigException($"A duplicate transformation property '{transformName}.{propertyName}' was found.");
                        }
                    }
                    else if (ContainsKey("Hosts", routeLabelKey, out _))
                    {
                        hosts = kvp.Value;
                    }
                    else if (ContainsKey($"Match{ConfigurationValues.KeyDelimiter}Path", routeLabelKey, out _))
                    {
                        path = kvp.Value;
                    }
                    else if (ContainsKey("Order", routeLabelKey, out _))
                    {
                        order = ConvertLabelValue<int?>(kvp.Key, kvp.Value);
                    }
                }

                var route = new ProxyRoute
                {
                    RouteId = $"{Uri.EscapeDataString(backendId)}:{Uri.EscapeDataString(routeNamePair.Value)}",
                    Match =
                    {
                        Hosts = SplitHosts(hosts),
                        Path = path,
                        Headers = headerMatches.Count > 0 ? headerMatches.Select(hm => hm.Value).ToArray() : null
                    },
                    Order = order,
                    ClusterId = backendId,
                    Metadata = metadata,
                    Transforms = transforms.Count > 0 ? transforms.Select(tr => tr.Value).ToList() : null
                };
                routes.Add(route);
            }

            return routes;
        }

        internal static Cluster BuildCluster(Uri serviceName, string endpointName, IDictionary<string, string> labels)
        {
            var clusterMetadata = new Dictionary<string, string>();
            Dictionary<string, string> sessionAffinitySettings = null;
            var backendKeyPrefix = $"{ConfigurationValues.EndpointsLabelPrefix}{ConfigurationValues.KeyDelimiter}{endpointName}{ConfigurationValues.KeyDelimiter}Backend{ConfigurationValues.KeyDelimiter}";
            var backendMetadataKeyPrefix = $"{backendKeyPrefix}Metadata{ConfigurationValues.KeyDelimiter}";
            var sessionAffinitySettingsKeyPrefix = $"{backendKeyPrefix}SessionAffinity{ConfigurationValues.KeyDelimiter}Settings{ConfigurationValues.KeyDelimiter}";
            foreach (var item in labels)
            {
                if (item.Key.StartsWith(backendMetadataKeyPrefix, StringComparison.Ordinal))
                {
                    clusterMetadata[item.Key.Substring(backendMetadataKeyPrefix.Length)] = item.Value;
                }
                else if (item.Key.StartsWith(sessionAffinitySettingsKeyPrefix, StringComparison.Ordinal))
                {
                    if (sessionAffinitySettings == null)
                    {
                        sessionAffinitySettings = new Dictionary<string, string>();
                    }

                    sessionAffinitySettings[item.Key.Substring(sessionAffinitySettingsKeyPrefix.Length)] = item.Value;
                }
            }

            var clusterId = GetClusterId(serviceName, endpointName, labels);

            var versionLabel = GetLabel<string>(labels, $"{backendKeyPrefix}HttpRequest{ConfigurationValues.KeyDelimiter}Version", null);
#if NET
            var versionPolicyLabel = GetLabel<string>(labels, $"{backendKeyPrefix}HttpRequest{ConfigurationValues.KeyDelimiter}VersionPolicy", null);
#endif
            var cluster = new Cluster
            {
                Id = clusterId,
                LoadBalancingPolicy = GetLabel<string>(labels, $"{backendKeyPrefix}LoadBalancingPolicy", null),
                SessionAffinity = new SessionAffinityOptions
                {
                    Enabled = GetLabel(labels, $"{backendKeyPrefix}SessionAffinity{ConfigurationValues.KeyDelimiter}Enabled", false),
                    Mode = GetLabel<string>(labels, $"{backendKeyPrefix}SessionAffinity{ConfigurationValues.KeyDelimiter}Mode", null),
                    FailurePolicy = GetLabel<string>(labels, $"{backendKeyPrefix}SessionAffinity{ConfigurationValues.KeyDelimiter}FailurePolicy", null),
                    Settings = sessionAffinitySettings
                },
                HttpRequest = new ProxyHttpRequestOptions
                {
                    Timeout = ToNullableTimeSpan(GetLabel<double?>(labels, $"{backendKeyPrefix}HttpRequest{ConfigurationValues.KeyDelimiter}Timeout", null)),
                    Version = !string.IsNullOrEmpty(versionLabel) ? Version.Parse(versionLabel + (versionLabel.Contains('.') ? "" : ".0")) : null,
#if NET
                    VersionPolicy = !string.IsNullOrEmpty(versionLabel) ? (HttpVersionPolicy)Enum.Parse(typeof(HttpVersionPolicy), versionPolicyLabel) : null
#endif
                },
                HealthCheck = new HealthCheckOptions
                {
                    Active = new ActiveHealthCheckOptions
                    {
                        Enabled = GetLabel(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Active{ConfigurationValues.KeyDelimiter}Enabled", false),
                        Interval = ToNullableTimeSpan(GetLabel<double?>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Active{ConfigurationValues.KeyDelimiter}Interval", null)),
                        Timeout = ToNullableTimeSpan(GetLabel<double?>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Active{ConfigurationValues.KeyDelimiter}Timeout", null)),
                        Path = GetLabel<string>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Active{ConfigurationValues.KeyDelimiter}Path", null),
                        Policy = GetLabel<string>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Active{ConfigurationValues.KeyDelimiter}Policy", null)
                    },
                    Passive = new PassiveHealthCheckOptions
                    {
                        Enabled = GetLabel(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Passive{ConfigurationValues.KeyDelimiter}Enabled", false),
                        Policy = GetLabel<string>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Passive{ConfigurationValues.KeyDelimiter}Policy", null),
                        ReactivationPeriod = ToNullableTimeSpan(GetLabel<double?>(labels, $"{backendKeyPrefix}HealthCheck{ConfigurationValues.KeyDelimiter}Passive{ConfigurationValues.KeyDelimiter}ReactivationPeriod", null))
                    }
                },
                Metadata = clusterMetadata,
            };
            return cluster;
        }

        private static TimeSpan? ToNullableTimeSpan(double? seconds)
        {
            return seconds.HasValue ? (TimeSpan?)TimeSpan.FromSeconds(seconds.Value) : null;
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

                    if (!_allowedRouteNamesRegex.IsMatch(name))
                    {
                        throw new ConfigException(
                            $"Invalid endpoint name '{name}', should only contain alphanumerical characters, underscores or hyphens.");
                    }

                    names.Add(name);
                }
            }
            return names;
        }

        private static bool ContainsKey(string expectedKeyName, ReadOnlySpan<char> actualKey, out ReadOnlySpan<char> keyRemainder)
        {
            keyRemainder = default;

            if (!actualKey.StartsWith(expectedKeyName, StringComparison.Ordinal))
            {
                return false;
            }

            keyRemainder = actualKey.Slice(expectedKeyName.Length);
            return true;
        }
    }
}