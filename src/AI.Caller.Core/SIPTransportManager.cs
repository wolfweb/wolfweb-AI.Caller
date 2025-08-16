using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorceryMedia.Abstractions;
using System.Net;
using System.Net.Sockets;
using System.Xml;

namespace AI.Caller.Core {
    public class SIPTransportManager {
        private static int SIP_DEFAULT_PORT = 7060;

        private static string? HOMER_SERVER_ADDRESS = null;
        private static int HOMER_SERVER_PORT = 9060;

        private bool _isInitialised = false;

        private readonly ILogger _logger;
        private readonly string? _contactHost;
        private readonly UdpClient? _homerSIPClient;

        public event Func<SIPRequest, Task<bool>>? IncomingCall;

        public SIPTransport? SIPTransport { get; private set; }

        public SIPTransportManager(string? contactHost, ILogger<SIPTransportManager> logger) {
            _logger = logger;
            _contactHost = contactHost;
            if (HOMER_SERVER_ADDRESS != null) {
                _homerSIPClient = new UdpClient(0, AddressFamily.InterNetwork);
            }
        }

        public void Shutdown() {
            if (SIPTransport != null) {
                SIPTransport.Shutdown();
            }
        }

        public async Task InitialiseSIP() {
            if (_isInitialised == false) {
                await Task.Run(() => {
                    _isInitialised = true;

                    bool IsPortAvailable(int port) {
                        try {
                            using (var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp)) {
                                socket.Bind(new IPEndPoint(IPAddress.Any, port));
                                return true;
                            }
                        } catch {
                            return false;
                        }
                    }

                    SIPTransport = new SIPTransport();
                    SIPUDPChannel? udpChannel = null;
                    try {
                        if (IsPortAvailable(SIP_DEFAULT_PORT)) {
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, SIP_DEFAULT_PORT));
                        } else {
                            _logger.LogWarning($"Port {SIP_DEFAULT_PORT} is not available, will use random port.");
                            udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }
                    } catch (ApplicationException bindExcp) {
                        _logger.LogWarning($"Socket exception attempting to bind UDP channel to port {SIP_DEFAULT_PORT}, will use random port. {bindExcp.Message}.");
                        udpChannel = new SIPUDPChannel(new IPEndPoint(IPAddress.Any, 0));
                    }

                    SIPTCPChannel? tcpChannel = null;
                    try {
                        if (IsPortAvailable(udpChannel.Port)) {
                            tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, udpChannel.Port));
                        } else {
                            _logger.LogWarning($"Port {udpChannel.Port} is not available for TCP, will use random port.");
                            tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, 0));
                        }
                    } catch (SocketException bindExcp) {
                        _logger.LogWarning($"Socket exception attempting to bind TCP channel to port {udpChannel.Port}, will use random port. {bindExcp.Message}.");
                        tcpChannel = new SIPTCPChannel(new IPEndPoint(IPAddress.Any, 0));
                    }
                    SIPTransport.AddSIPChannel(new List<SIPChannel> { tcpChannel, udpChannel });
                    if (!string.IsNullOrEmpty(_contactHost)) {
                        SIPTransport.ContactHost = _contactHost;
                        _logger.LogDebug($"SIP ContactHost configured as: {_contactHost}, but using local address for carrier compatibility");
                    }
                });
                if (SIPTransport != null) {
                    SIPTransport.SIPTransportRequestReceived += SIPTransportRequestReceived;

                    SIPTransport.SIPRequestInTraceEvent += SIPRequestInTraceEvent;
                    SIPTransport.SIPRequestOutTraceEvent += SIPRequestOutTraceEvent;
                    SIPTransport.SIPResponseInTraceEvent += SIPResponseInTraceEvent;
                    SIPTransport.SIPResponseOutTraceEvent += SIPResponseOutTraceEvent;
                }
            }
        }

        private async Task SIPTransportRequestReceived(SIPEndPoint localSIPEndPoint, SIPEndPoint remoteEndPoint, SIPRequest sipRequest) {
            if (SIPTransport == null) throw new Exception("SIPTransport should init");

            if (sipRequest.Method == SIPMethodsEnum.INFO) {
                _logger.LogDebug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await SIPTransport.SendResponseAsync(notAllowedResponse);
            } else if (sipRequest.Method == SIPMethodsEnum.ACK) {
                _logger.LogDebug($"*** ACK MESSAGE RECEIVED *** CallId: {sipRequest.Header.CallId}, From: {sipRequest.Header.From?.FromURI?.User}, To: {sipRequest.Header.To?.ToURI?.User}");
            } else if (sipRequest.Method == SIPMethodsEnum.BYE) {
                _logger.LogDebug($"*** BYE MESSAGE RECEIVED *** CallId: {sipRequest.Header.CallId}, From: {sipRequest.Header.From?.FromURI?.User}, To: {sipRequest.Header.To?.ToURI?.User}");
            } else if (sipRequest.Header.From != null &&
                  sipRequest.Header.From.FromTag != null &&
                  sipRequest.Header.To != null &&
                  sipRequest.Header.To.ToTag != null) {
                _logger.LogDebug($"SIP {sipRequest.Method} request for established dialogue received, letting SIPClient handle.");
            } else if (sipRequest.Method == SIPMethodsEnum.INVITE) {
                _logger.LogDebug($"*** Processing new INVITE *** CallId: {sipRequest.Header.CallId}, From: {sipRequest.Header.From?.FromURI?.User}, To: {sipRequest.Header.To?.ToURI?.User}");
                bool? callAccepted = await IncomingCall?.Invoke(sipRequest);
                if (callAccepted == false) {
                    _logger.LogDebug($"Call rejected for CallId: {sipRequest.Header.CallId}");
                    UASInviteTransaction uasTransaction = new UASInviteTransaction(SIPTransport, sipRequest, null);
                    SIPResponse busyResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.BusyHere, null);
                    uasTransaction.SendFinalResponse(busyResponse);
                } else if (callAccepted == true) {
                    _logger.LogDebug($"Call accepted for CallId: {sipRequest.Header.CallId}");
                } else {
                    _logger.LogWarning($"Call processing returned null for CallId: {sipRequest.Header.CallId}");
                }
            } else {
                _logger.LogDebug("SIP " + sipRequest.Method + " request received but no processing has been set up for it, rejecting.");
                SIPResponse notAllowedResponse = SIPResponse.GetResponse(sipRequest, SIPResponseStatusCodesEnum.MethodNotAllowed, null);
                await SIPTransport.SendResponseAsync(notAllowedResponse);
            }
        }

        private void SIPRequestInTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest sipRequest) {
            _logger.LogDebug($"Request Received {localEP}<-{remoteEP}: {sipRequest.StatusLine}.");

            if (sipRequest.Header.Contact != null && sipRequest.Header.Contact.Count > 0) {
                foreach (var contact in sipRequest.Header.Contact) {
                    _logger.LogDebug($"SIP Request Contact Header: {contact.ContactURI}");
                }
            }

            if (_homerSIPClient != null) {
            }
        }

        private void SIPRequestOutTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPRequest sipRequest) {
            _logger.LogDebug($"Request Sent {localEP}<-{remoteEP}: {sipRequest.StatusLine}.");

            if (_homerSIPClient != null) {
            }
        }

        private void SIPResponseInTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPResponse sipResponse) {
            _logger.LogDebug($"Response Received {localEP}<-{remoteEP}: {sipResponse.ShortDescription}.");

            if (_homerSIPClient != null) {
               
            }
        }

        private void SIPResponseOutTraceEvent(SIPEndPoint localEP, SIPEndPoint remoteEP, SIPResponse sipResponse) {
            _logger.LogDebug($"Response Sent {localEP}<-{remoteEP}: {sipResponse.ShortDescription}.");

            if (_homerSIPClient != null) {
            }
        }        
    }
}
