using AI.Caller.Core;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace AI.Caller.Phone.Controllers {
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class WebRTCController : ControllerBase {
        private readonly WebRTCSettings _webRTCSettings;
        private readonly ILogger<WebRTCController> _logger;

        public WebRTCController(
            IOptions<WebRTCSettings> webRTCSettings,
            ILogger<WebRTCController> logger) {
            _webRTCSettings = webRTCSettings.Value;
            _logger = logger;
        }

        [HttpGet("ice-servers")]
        public ActionResult<ClientIceConfiguration> GetIceServers() {
            _logger.LogInformation("ICE server configuration requested");

            var clientConfig = new ClientIceConfiguration {
                IceServers = _webRTCSettings.IceServers.Select(server => new ClientIceServer {
                    Urls = GetUrlsList(server.Urls),
                    Username = server.Username,
                    Credential = server.Credential
                }).ToList(),
                IceTransportPolicy = _webRTCSettings.IceTransportPolicy
            };

            _logger.LogInformation($"Returning {clientConfig.IceServers.Count} ICE servers");
            return clientConfig;
        }

        private static List<string> GetUrlsList(object urls) {
            return urls switch {
                string singleUrl => new List<string> { singleUrl },
                string[] urlArray => urlArray.ToList(),
                IEnumerable<string> urlEnumerable => urlEnumerable.ToList(),
                _ => new List<string>()
            };
        }
    }

    public class ClientIceConfiguration {
        public List<ClientIceServer> IceServers { get; set; } = new List<ClientIceServer>();

        public string IceTransportPolicy { get; set; } = "all";
    }

    public class ClientIceServer {
        public List<string> Urls { get; set; } = new List<string>();

        public string? Username { get; set; }

        public string? Credential { get; set; }
    }
}