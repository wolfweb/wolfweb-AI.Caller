using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Caller.Core
{
    /// <summary>
    /// Configuration settings for WebRTC connections, including STUN/TURN servers
    /// </summary>
    public class WebRTCSettings
    {
        /// <summary>
        /// List of ICE server configurations that can be bound from appsettings.json
        /// </summary>
        public List<IceServerConfig> IceServers { get; set; } = new List<IceServerConfig>();
        
        /// <summary>
        /// ICE transport policy: "all" to try all connection methods, or "relay" to force TURN relay
        /// </summary>
        public string IceTransportPolicy { get; set; } = "all";

        /// <summary>
        /// Converts the configuration-bindable IceServerConfig instances to RTCIceServer instances
        /// required by the SIPSorcery library.
        /// </summary>
        /// <returns>List of RTCIceServer instances for use with WebRTC peer connections</returns>
        /// <exception cref="ArgumentException">Thrown when server configuration is invalid</exception>
        public List<RTCIceServer> GetRTCIceServers()
        {
            var rtcIceServers = new List<RTCIceServer>();

            foreach (var config in IceServers)
            {
                try
                {
                    if (config.Urls == null || config.Urls.Length == 0)
                    {
                        throw new ArgumentException("ICE server configuration must have at least one URL");
                    }

                    var rtcServer = new RTCIceServer
                    {
                        urls = config.Urls
                    };

                    // Set credentials if provided
                    if (!string.IsNullOrWhiteSpace(config.Username))
                    {
                        rtcServer.username = config.Username;
                    }

                    if (!string.IsNullOrWhiteSpace(config.Credential))
                    {
                        rtcServer.credential = config.Credential;
                    }

                    rtcIceServers.Add(rtcServer);
                }
                catch (Exception ex)
                {
                    throw new ArgumentException($"Failed to convert ICE server configuration: {ex.Message}", ex);
                }
            }

            return rtcIceServers;
        }
    }
}