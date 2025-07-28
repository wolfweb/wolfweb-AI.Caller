using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using AI.Caller.Core.Recording;
using AI.Caller.Core.Network;

namespace AI.Caller.Core {
    public class SIPClient {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static int TRANSFER_RESPONSE_TIMEOUT_SECONDS = 10;

        private readonly ILogger _logger;
        private readonly string _sipServer;
        private readonly SIPUserAgent m_userAgent;
        private readonly SIPTransport m_sipTransport;
        private readonly WebRTCSettings? _webRTCSettings;

        private readonly string _clientId;
        private readonly IAudioBridge? _audioBridge;
        private readonly INetworkMonitoringService? _networkMonitoringService;

        public event Action<SIPClient>? CallAnswer;
        public event Action<SIPClient>? CallEnded;
        public event Action<SIPClient>? RemotePutOnHold;
        public event Action<SIPClient>? RemoteTookOffHold;
        public event Action<SIPClient, string>? StatusMessage;

        public event Action<SIPClient>? HangupInitiated;
        public event Action<SIPClient>? AudioStopped;
        public event Action<SIPClient>? ResourcesReleased;

        public SIPDialogue Dialogue => m_userAgent.Dialogue;
        public bool IsCallActive => m_userAgent.IsCallActive;
        public bool IsOnHold => m_userAgent.IsOnLocalHold || m_userAgent.IsOnRemoteHold;
        public RTPSession? MediaSession { get; private set; }
        public RTCPeerConnection? RTCPeerConnection { get; private set; }

        private CancellationTokenSource _cts = new();
        private SIPServerUserAgent? m_pendingIncomingCall;

        public SIPClient(
            string sipServer,
            ILogger logger,
            SIPTransport sipTransport,
            IAudioBridge? audioBridge = null,
            WebRTCSettings? webRTCSettings = null,
            INetworkMonitoringService? networkMonitoringService = null
        ) {
            _logger = logger;
            _sipServer = sipServer;
            m_sipTransport = sipTransport;
            _audioBridge = audioBridge;
            _webRTCSettings = webRTCSettings;
            _networkMonitoringService = networkMonitoringService;
            _clientId = $"SIPClient_{Guid.NewGuid():N}[{sipServer}]";

            m_userAgent = new(m_sipTransport, null);

            m_userAgent.ClientCallFailed += CallFailed;
            m_userAgent.ClientCallTrying += CallTrying;
            m_userAgent.ClientCallRinging += CallRinging;
            m_userAgent.ClientCallAnswered += CallAnswered;

            m_userAgent.OnDtmfTone += OnDtmfTone;
            m_userAgent.OnCallHungup += CallFinished;
            m_userAgent.OnTransferNotify += OnTransferNotify;

            m_userAgent.ServerCallCancelled += IncomingCallCancelled;

            // 注册到网络监控服务
            RegisterWithNetworkMonitoring();
        }

        public async Task CallAsync(string destination, SIPFromHeader fromHeader) {
            SIPURI callURI;

            if (destination.Contains("@")) {
                callURI = SIPURI.ParseSIPURIRelaxed(destination);
            } else {
                callURI = SIPURI.ParseSIPURIRelaxed(destination + "@" + _sipServer);
            }

            StatusMessage?.Invoke(this, $"Starting call to {callURI}.");

            var dstEndpoint = await SIPDns.ResolveAsync(callURI, false, _cts.Token);

            if (dstEndpoint == null) {
                StatusMessage?.Invoke(this, $"Call failed, could not resolve {callURI}.");
            } else {
                StatusMessage?.Invoke(this, $"Call progressing, resolved {callURI} to {dstEndpoint}.");
                Debug.WriteLine($"DNS lookup result for {callURI}: {dstEndpoint}.");
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(null, null, callURI.ToString(), fromHeader.ToString(), null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, null, null);

                MediaSession = await CreateMediaSession();
                MediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                await m_userAgent.InitiateCallAsync(callDescriptor, MediaSession);
            }
        }

        private void OnForwardMediaToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    return;
                }

                if (MediaSession != null) {
                    MediaSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                    _logger.LogTrace($"Forwarded WebRTC audio to SIP: {rtpPacket.Payload.Length} bytes from {remote}");
                } else {
                    _logger.LogTrace($"Cannot forward audio to SIP: MediaSession={MediaSession != null}, CallActive={m_userAgent.IsCallActive}");
                }

                if (_audioBridge != null) {
                    var audioFormat = GetAudioFormat();
                    _audioBridge.ForwardAudioData(AudioSource.WebRTC_Outgoing, rtpPacket.Payload, audioFormat);
                }
            } catch (Exception ex) {
                _logger.LogError($"Error forwarding media to SIP: {ex.Message}");
            }
        }

        private void OnRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    return;
                }

                if (RTCPeerConnection != null && RTCPeerConnection.connectionState == RTCPeerConnectionState.connected) {
                    RTCPeerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                    _logger.LogTrace($"Forwarded RTP audio to WebRTC: {rtpPacket.Payload.Length} bytes from {remote}");
                }

                if (_audioBridge != null) {
                    var audioFormat = GetAudioFormat();
                    _audioBridge.ForwardAudioData(AudioSource.RTP_Incoming, rtpPacket.Payload, audioFormat);
                }
            } catch (Exception ex) {
                _logger.LogError($"Error processing RTP audio packet from {remote}: {ex.Message}");
            }
        }

        public void Cancel() {
            StatusMessage?.Invoke(this, "Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
            m_userAgent.Cancel();
        }

        public void Accept(SIPRequest sipRequest) {
            m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
        }

        public async Task<bool> AnswerAsync() {
            if (m_pendingIncomingCall == null) {
                StatusMessage?.Invoke(this, $"There was no pending call available to answer.");
                return false;
            } else {
                var sipRequest = m_pendingIncomingCall.ClientTransaction.TransactionRequest;

                bool hasAudio = true;
                bool hasVideo = false;

                if (sipRequest.Body != null) {
                    SDP offerSDP = SDP.ParseSDPDescription(sipRequest.Body);
                    hasAudio = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                    hasVideo = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.video && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                }

                if (MediaSession == null) {
                    MediaSession = await CreateMediaSession();
                    MediaSession.OnRtpPacketReceived += OnRtpPacketReceived;
                }

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                bool result = await m_userAgent.Answer(m_pendingIncomingCall, MediaSession);
                m_pendingIncomingCall = null;

                return result;
            }
        }

        public void Redirect(string destination) {
            m_pendingIncomingCall?.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        public async Task<RTCSessionDescriptionInit> CreateOfferAsync() {
            CreatePeerConnection();

            if (RTCPeerConnection == null) throw new Exception("初始化 RTCPeerConnection 异常");
            var offerSdp = RTCPeerConnection.createOffer();
            await RTCPeerConnection.setLocalDescription(offerSdp);
            return offerSdp;
        }

        public async Task<RTCSessionDescriptionInit?> OfferAsync(RTCSessionDescriptionInit sdpOffer) {
            CreatePeerConnection();

            if (RTCPeerConnection == null) throw new Exception("初始化 RTCPeerConnection 异常");

            var result = RTCPeerConnection.setRemoteDescription(sdpOffer);
            if (result == SetDescriptionResultEnum.OK) {
                var answerSdp = RTCPeerConnection.createAnswer(new RTCAnswerOptions { });

                await RTCPeerConnection.setLocalDescription(answerSdp);
                return answerSdp;
            }
            return null;
        }

        public void Reject() {
            m_pendingIncomingCall?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        public void Hangup() {
            if (m_userAgent.IsCallActive) {
                try {
                    HangupInitiated?.Invoke(this);
                    StatusMessage?.Invoke(this, "Hangup initiated.");

                    StopAudioStreams();
                    m_userAgent.Hangup();
                } catch (Exception ex) {
                    _logger.LogError($"Error in Hangup: {ex.Message}");
                } finally {
                    CallFinished(null);
                }
            }
        }

        public void StopAudioStreams() {
            try {
                if (MediaSession != null) {
                    MediaSession.OnRtpPacketReceived -= OnRtpPacketReceived;
                    StatusMessage?.Invoke(this, "Audio streams stopped.");
                }

                if (RTCPeerConnection != null) {
                    RTCPeerConnection.OnRtpPacketReceived -= OnForwardMediaToSIP;
                }

                AudioStopped?.Invoke(this);
            } catch (Exception ex) {
                _logger.LogError($"Error stopping audio streams: {ex.Message}");
            }
        }

        public void ReleaseMediaResources() {
            bool rtcConnectionClosed = false;
            bool mediaSessionClosed = false;

            if (RTCPeerConnection != null) {
                try {
                    RTCPeerConnection.close();
                    RTCPeerConnection = null;
                    rtcConnectionClosed = true;
                    StatusMessage?.Invoke(this, "RTCPeerConnection resources released successfully.");
                } catch (Exception ex) {
                    _logger.LogError($"Error closing RTCPeerConnection : {ex.Message}");
                    StatusMessage?.Invoke(this, $"Warning: RTCPeerConnection close failed: {ex.Message}");
                    RTCPeerConnection = null;
                }
            }

            if (MediaSession != null) {
                try {
                    MediaSession.Close("Resources released");
                    MediaSession = null;
                    mediaSessionClosed = true;
                    StatusMessage?.Invoke(this, "MediaSession resources released successfully.");
                } catch (Exception ex) {
                    _logger.LogError($"Error closing MediaSession : {ex.Message}");
                    StatusMessage?.Invoke(this, $"Warning: MediaSession close failed: {ex.Message}");
                    MediaSession = null;
                }
            }

            if (rtcConnectionClosed || mediaSessionClosed || RTCPeerConnection == null || MediaSession == null) {
                try {
                    ResourcesReleased?.Invoke(this);
                } catch (Exception ex) {
                    _logger.LogError($"Error triggering ResourcesReleased event : {ex.Message}");
                }
            }
        }

        public Task<bool> BlindTransferAsync(string destination) {
            if (SIPURI.TryParse(destination, out var uri)) {
                return m_userAgent.BlindTransfer(uri, TimeSpan.FromSeconds(TRANSFER_RESPONSE_TIMEOUT_SECONDS), _cts.Token);
            } else {
                StatusMessage?.Invoke(this, $"The transfer destination was not a valid SIP URI.");
                return Task.FromResult(false);
            }
        }

        public Task<bool> AttendedTransferAsync(SIPDialogue transferee) {
            return m_userAgent.AttendedTransfer(transferee, TimeSpan.FromSeconds(TRANSFER_RESPONSE_TIMEOUT_SECONDS), _cts.Token);
        }

        public void Shutdown() {
            _cts.Cancel();
            Hangup();
            
            // 从网络监控服务注销
            UnregisterFromNetworkMonitoring();
        }

        private async Task<RTPSession> CreateMediaSession() {
            var rtpSession = new RTPSession(false, false, false);
            rtpSession.AcceptRtpFromAny = true;
            MediaStreamTrack audioTrack = new(
                SDPMediaTypesEnum.audio,
                false,
                [
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                ]
            );
            rtpSession.addTrack(audioTrack);

            await rtpSession.Start();

            return rtpSession;
        }

        private RTCConfiguration GetRTCConfiguration() {
            var iceServers = new List<RTCIceServer>();

            if (_webRTCSettings != null && _webRTCSettings.IceServers != null && _webRTCSettings.IceServers.Count > 0) {
                try {
                    iceServers.AddRange(_webRTCSettings.GetRTCIceServers());
                    _logger.LogInformation($"Using {iceServers.Count} configured ICE servers for WebRTC");
                } catch (ArgumentException ex) {
                    _logger.LogError(ex, "Failed to convert WebRTC settings to RTCIceServer instances. Using default STUN server.");
                }
            }

            if (iceServers.Count == 0) {
                _logger.LogWarning("No ICE servers configured, using default STUN server");
            }

            var iceTransportPolicy = RTCIceTransportPolicy.all;
            if (_webRTCSettings != null &&
                !string.IsNullOrEmpty(_webRTCSettings.IceTransportPolicy) &&
                _webRTCSettings.IceTransportPolicy.Equals("relay", StringComparison.OrdinalIgnoreCase)) {
                iceTransportPolicy = RTCIceTransportPolicy.relay;
                _logger.LogInformation("Using 'relay' ICE transport policy");
            }

            return new RTCConfiguration {
                iceServers = iceServers.ToList(),
                iceTransportPolicy = iceTransportPolicy,
                X_DisableExtendedMasterSecretKey = true
            };
        }

        private void CreatePeerConnection() {
            var pcConfiguration = GetRTCConfiguration();
            var peerConnection = new RTCPeerConnection(pcConfiguration);

            SetupPeerConnectionEvents(peerConnection);

            RTCPeerConnection = peerConnection;
        }

        private void SetupPeerConnectionEvents(RTCPeerConnection peerConnection) {
            if (peerConnection == null)
                return;

            MediaStreamTrack track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                [
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                ]
            );
            peerConnection.addTrack(track);

            peerConnection.OnRtpPacketReceived += OnForwardMediaToSIP;

            peerConnection.onconnectionstatechange += (state) => {
                if (
                    state == RTCPeerConnectionState.closed ||
                    state == RTCPeerConnectionState.disconnected ||
                    state == RTCPeerConnectionState.failed
                ) {
                    CallFinished(null);
                } else if (state == RTCPeerConnectionState.connected) {
                    StatusMessage?.Invoke(this, "Peer connection connected.");
                }
            };

            peerConnection.oniceconnectionstatechange += (state) => {
                _logger.LogInformation($"ICE connection state changed to: {state}");
            };

            peerConnection.onicegatheringstatechange += (state) => {
                _logger.LogInformation($"ICE gathering state changed to: {state}");
            };

            peerConnection.onicecandidate += (candidate) => {
                if (candidate != null) {
                    _logger.LogDebug($"ICE candidate: {candidate.candidate}, Type: {candidate.type}");

                    if (candidate.candidate != null && candidate.candidate.Contains("typ relay")) {
                        _logger.LogInformation("Using TURN relay for media");
                        StatusMessage?.Invoke(this, "Using TURN relay for media connection");
                    }
                }
            };
        }

        private void CallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            StatusMessage?.Invoke(this, "Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        private void CallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            StatusMessage?.Invoke(this, "Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
        }

        private void CallFailed(ISIPClientUserAgent uac, string errorMessage, SIPResponse failureResponse) {
            StatusMessage?.Invoke(this, "Call failed: " + errorMessage + ".");
            CallFinished(null);
        }

        private void CallAnswered(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            StatusMessage?.Invoke(this, "Call answered: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            CallAnswer?.Invoke(this);
        }

        private void CallFinished(SIPDialogue? dialogue) {
            try {
                m_pendingIncomingCall = null;
                ReleaseMediaResources();
                StatusMessage?.Invoke(this, "Call finished and resources cleaned up.");
            } catch (Exception ex) {
                _logger.LogError($"Error in CallFinished : {ex.Message}");
                StatusMessage?.Invoke(this, $"Error during call cleanup: {ex.Message}");
            } finally {
                try {
                    CallEnded?.Invoke(this);
                    _logger.LogInformation($"CallEnded event triggered ");
                } catch (Exception ex) {
                    _logger.LogError($"Error triggering CallEnded event : {ex.Message}");
                }
            }
        }

        private void IncomingCallCancelled(ISIPServerUserAgent uas, SIPRequest cancelRequest) {
            CallFinished(null);
        }

        private void OnTransferNotify(string sipFrag) {
            if (sipFrag.Contains("SIP/2.0 200") == true) {
                Hangup();
            } else {
                Match statusCodeMatch = Regex.Match(sipFrag, @"^SIP/2\.0 (?<statusCode>\d{3})");
                if (statusCodeMatch.Success) {
                    int statusCode = Int32.Parse(statusCodeMatch.Result("${statusCode}"));
                    SIPResponseStatusCodesEnum responseStatusCode = (SIPResponseStatusCodesEnum)statusCode;
                    StatusMessage?.Invoke(this, $"Transfer failed {responseStatusCode}");
                }
            }
        }

        private void OnDtmfTone(byte dtmfKey, int duration) {
            StatusMessage?.Invoke(this, $"DTMF event from remote call party {dtmfKey} duration {duration}.");
        }

        private void OnRemotePutOnHold() {
            RemotePutOnHold?.Invoke(this);
        }

        private void OnRemoteTookOffHold() {
            RemoteTookOffHold?.Invoke(this);
        }

        public Task SendDTMFAsync(byte tone) {
            if (m_userAgent != null) {
                return m_userAgent.SendDtmf(tone);
            } else {
                return Task.FromResult(0);
            }
        }

        private Recording.AudioFormat GetAudioFormat() {
            return new Recording.AudioFormat(8000, 1, 16, AudioSampleFormat.PCM);
        }

        /// <summary>
        /// 注册到网络监控服务
        /// </summary>
        private void RegisterWithNetworkMonitoring()
        {
            try
            {
                if (_networkMonitoringService != null)
                {
                    _networkMonitoringService.RegisterSipClient(_clientId, this);
                    _logger.LogDebug("SIP client {ClientId} registered with network monitoring service", _clientId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to register SIP client {ClientId} with network monitoring service: {Message}", 
                    _clientId, ex.Message);
            }
        }

        /// <summary>
        /// 从网络监控服务注销
        /// </summary>
        private void UnregisterFromNetworkMonitoring()
        {
            try
            {
                if (_networkMonitoringService != null)
                {
                    _networkMonitoringService.UnregisterSipClient(_clientId);
                    _logger.LogDebug("SIP client {ClientId} unregistered from network monitoring service", _clientId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to unregister SIP client {ClientId} from network monitoring service: {Message}", 
                    _clientId, ex.Message);
            }
        }

        /// <summary>
        /// 获取客户端ID
        /// </summary>
        public string GetClientId()
        {
            return _clientId;
        }
    }
}