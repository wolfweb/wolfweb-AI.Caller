using AI.Caller.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using SIPSorcery.Net;
using System.Collections.Generic;
using System.Linq;

namespace AI.Caller.Phone.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WebRTCController : ControllerBase
    {
        private readonly WebRTCSettings _webRTCSettings;
        private readonly ILogger<WebRTCController> _logger;

        public WebRTCController(
            IOptions<WebRTCSettings> webRTCSettings,
            ILogger<WebRTCController> logger)
        {
            _webRTCSettings = webRTCSettings.Value;
            _logger = logger;
        }

        /// <summary>
        /// Get ICE server configuration for WebRTC
        /// </summary>
        /// <returns>ICE server configuration for client-side WebRTC</returns>
        [HttpGet("ice-servers")]
        public ActionResult<ClientIceConfiguration> GetIceServers()
        {
            _logger.LogInformation("ICE server configuration requested");
            
            var clientConfig = new ClientIceConfiguration
            {
                IceServers = _webRTCSettings.IceServers.Select(server => new ClientIceServer
                {
                    Urls = GetUrlsList(server.Urls),
                    Username = server.Username,
                    Credential = server.Credential
                }).ToList(),
                IceTransportPolicy = _webRTCSettings.IceTransportPolicy
            };
            
            _logger.LogInformation($"Returning {clientConfig.IceServers.Count} ICE servers");
            return clientConfig;
        }

        private static List<string> GetUrlsList(object urls)
        {
            return urls switch
            {
                string singleUrl => new List<string> { singleUrl },
                string[] urlArray => urlArray.ToList(),
                IEnumerable<string> urlEnumerable => urlEnumerable.ToList(),
                _ => new List<string>()
            };
        }
    }

    /// <summary>
    /// Client-side ICE configuration
    /// </summary>
    public class ClientIceConfiguration
    {
        /// <summary>
        /// List of ICE servers
        /// </summary>
        public List<ClientIceServer> IceServers { get; set; } = new List<ClientIceServer>();
        
        /// <summary>
        /// ICE transport policy
        /// </summary>
        public string IceTransportPolicy { get; set; } = "all";
    }

    /// <summary>
    /// Client-side ICE server configuration
    /// </summary>
    public class ClientIceServer
    {
        /// <summary>
        /// List of URLs for this ICE server
        /// </summary>
        public List<string> Urls { get; set; } = new List<string>();
        
        /// <summary>
        /// Username for TURN server authentication
        /// </summary>
        public string? Username { get; set; }
        
        /// <summary>
        /// Credential for TURN server authentication
        /// </summary>
        public string? Credential { get; set; }
    }
}