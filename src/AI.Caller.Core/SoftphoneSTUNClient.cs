using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using System.Net;

namespace AI.Caller.Core {
    public class SoftphoneSTUNClient {
        private Timer? updateTimer;

        private readonly ILogger _logger;

        private readonly TimeSpan updateIntervalNormal = TimeSpan.FromMinutes(1);

        private readonly TimeSpan updateIntervalShort = TimeSpan.FromSeconds(5);

        private readonly string m_stunServerHostname;

        private volatile bool m_stop;

        public event Action<IPAddress>? PublicIPAddressDetected;

        public SoftphoneSTUNClient(string stunServerHostname, ILogger logger) {
            _logger = logger;
            m_stunServerHostname = stunServerHostname;
        }

        public void Run() {
            m_stop = false;
            updateTimer = new Timer(e => {
                if (!m_stop) {
                    var publicIPAddress = GetPublicIPAddress();
                    if (publicIPAddress != null) {
                        PublicIPAddressDetected?.Invoke(publicIPAddress);
                    }

                    var timerInterval = (publicIPAddress == null) ? updateIntervalShort : updateIntervalNormal;
                    updateTimer!.Change(timerInterval, timerInterval);
                }
            }, null, TimeSpan.Zero, updateIntervalNormal);

            _logger.LogDebug("STUN client started.");
        }

        public void Stop() {
            m_stop = true;
            updateTimer!.Change(Timeout.Infinite, Timeout.Infinite);

            _logger.LogWarning("STUN client stopped.");
        }

        private IPAddress? GetPublicIPAddress() {
            try {
                var publicIP = STUNClient.GetPublicIPAddress(m_stunServerHostname);
                if (publicIP != null) {
                    _logger.LogDebug($"The STUN client was able to determine the public IP address as {publicIP}");
                } else {
                    _logger.LogDebug("The STUN client could not determine the public IP address.");
                }

                return publicIP;
            } catch (Exception getAddrExcp) {
                _logger.LogError("Exception GetPublicIPAddress. " + getAddrExcp.Message);
                return null;
            }
        }
    }
}
