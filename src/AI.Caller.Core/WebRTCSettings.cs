using SIPSorcery.Net;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AI.Caller.Core {
    public class WebRTCSettings {
        public List<IceServerConfig> IceServers { get; set; } = new List<IceServerConfig>();

        public string IceTransportPolicy { get; set; } = "all";

        public List<RTCIceServer> GetRTCIceServers() {
            var rtcIceServers = new List<RTCIceServer>();

            foreach (var config in IceServers) {
                try {
                    if (config.Urls == null || config.Urls.Length == 0) {
                        throw new ArgumentException("ICE server configuration must have at least one URL");
                    }

                    var rtcServer = new RTCIceServer {
                        urls = config.Urls
                    };

                    // Set credentials if provided
                    if (!string.IsNullOrWhiteSpace(config.Username)) {
                        rtcServer.username = config.Username;
                    }

                    if (!string.IsNullOrWhiteSpace(config.Credential)) {
                        rtcServer.credential = config.Credential;
                    }

                    rtcIceServers.Add(rtcServer);
                } catch (Exception ex) {
                    throw new ArgumentException($"Failed to convert ICE server configuration: {ex.Message}", ex);
                }
            }

            return rtcIceServers;
        }
    }
}