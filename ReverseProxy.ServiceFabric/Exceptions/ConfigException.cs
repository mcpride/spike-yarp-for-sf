// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.ReverseProxy.ServiceFabric
{
    /// <summary>
    /// Represents errors related to a user's configuration for the Island Gateway to use.
    /// </summary>
    public sealed class ConfigException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigException"/> class.
        /// </summary>
        public ConfigException()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigException"/> class.
        /// </summary>
        public ConfigException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigException"/> class.
        /// </summary>
        public ConfigException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
