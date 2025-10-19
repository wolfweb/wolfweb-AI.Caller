using AI.Caller.Core.Network;
using AI.Caller.Core.Models;
using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;

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
        private readonly bool _enableWebRtcBridging;
        private readonly INetworkMonitoringService? _networkMonitoringService;

        public event Action<SIPClient>? CallAnswered;
        public event Action<SIPClient>? CallEnded;
        public event Action<SIPClient>? CallTrying;
        public event Action<SIPClient>? CallRinging;
        public event Action<SIPClient>? RemotePutOnHold;
        public event Action<SIPClient>? RemoteTookOffHold;
        public event Action<SIPClient, string>? StatusMessage;

        public event Action<SIPClient>? HangupInitiated;
        public event Action<SIPClient>? AudioStopped;
        public event Action<SIPClient>? ResourcesReleased;
        public event Action<SIPClient, string>? CallInitiated;
        public event Action<SIPClient, HangupEventContext>? CallFinishedWithContext;

        public SIPDialogue Dialogue => m_userAgent.Dialogue;
        public bool IsCallActive => m_userAgent.IsCallActive;
        public bool IsOnHold => m_userAgent.IsOnLocalHold || m_userAgent.IsOnRemoteHold;

        private MediaSessionManager? _mediaManager;
        private CancellationTokenSource _cts = new();
        private SIPServerUserAgent? m_pendingIncomingCall;
        private string? _lastRemoteSdp = null;
        private bool _localHangupInitiated = false;

        public SIPClient(
            string sipServer,
            ILogger logger,
            SIPTransport sipTransport,
            WebRTCSettings? webRTCSettings = null,
            INetworkMonitoringService? networkMonitoringService = null,
            bool enableWebRtcBridging = true
        ) {
            _logger = logger;
            _sipServer = sipServer;
            m_sipTransport = sipTransport;
            _webRTCSettings = webRTCSettings;            
            _networkMonitoringService = networkMonitoringService;
            _enableWebRtcBridging = enableWebRtcBridging;
            _clientId = $"SIPClient_{Guid.NewGuid():N}[{sipServer}]";

            m_userAgent = new(m_sipTransport, null);

            m_userAgent.ClientCallFailed += OnCallFailed;
            m_userAgent.ClientCallTrying += OnCallTrying;
            m_userAgent.ClientCallRinging += OnCallRinging;
            m_userAgent.ClientCallAnswered += OnCallAnswered;

            m_userAgent.OnDtmfTone += OnDtmfTone;
            m_userAgent.OnCallHungup += CallFinished;
            m_userAgent.OnTransferNotify += OnTransferNotify;

            m_userAgent.ServerCallCancelled += IncomingCallCancelled;

            RegisterWithNetworkMonitoring();
        }

        public async Task CallAsync(string destination, SIPFromHeader fromHeader) {            
            SIPURI callURI = destination.Contains("@") ? SIPURI.ParseSIPURIRelaxed(destination) : SIPURI.ParseSIPURIRelaxed(destination + "@" + _sipServer);
            
            StatusMessage?.Invoke(this, $"Starting call to {callURI}.");

            var dstEndpoint = await SIPDns.ResolveAsync(callURI, false, _cts.Token);

            if (dstEndpoint == null) {
                StatusMessage?.Invoke(this, $"Call failed, could not resolve {callURI}.");
                return;
            }

            StatusMessage?.Invoke(this, $"Call progressing, resolved {callURI} to {dstEndpoint}.");
            Debug.WriteLine($"DNS lookup result for {callURI}: {dstEndpoint}.");
            SIPCallDescriptor callDescriptor = new SIPCallDescriptor(null, null, callURI.ToString(), fromHeader.ToString(), null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, null, null);

            StatusMessage?.Invoke(this, "Creating fresh MediaSessionManager for call...");
            EnsureMediaSessionInitialized();

            StatusMessage?.Invoke(this, "Initializing SIP media session...");
            _mediaManager!.InitializeMediaSession();

            StatusMessage?.Invoke(this, "SIP media session initialized, RTCPeerConnection will be created when needed.");
            _logger.LogDebug("MediaSessionManager ready, RTCPeerConnection will be lazy-initialized");

            m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
            m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

            StatusMessage?.Invoke(this, "Starting SIP call...");
            await m_userAgent.InitiateCallAsync(callDescriptor, _mediaManager.MediaSession);
            await _mediaManager.MediaSession!.Start();
        }

        public void Cancel() {
            StatusMessage?.Invoke(this, "Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
            m_userAgent.Cancel();
        }

        public void Accept(SIPRequest sipRequest) {
            m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
            EnsureMediaSessionInitialized();
        }

        public async Task<bool> SendSessionProgressAsync() {
            if (m_pendingIncomingCall == null) {
                _logger.LogWarning("No pending incoming call to send session progress");
                return false;
            }

            try {
                var sipRequest = m_pendingIncomingCall.ClientTransaction.TransactionRequest;
                
                if (string.IsNullOrEmpty(sipRequest.Body)) {
                    _logger.LogWarning("INVITE request has no SDP body, cannot establish Early Media");
                    return false;
                }

                EnsureMediaSessionInitialized();
                _mediaManager!.InitializeMediaSession();

                var remoteSdp = SDP.ParseSDPDescription(sipRequest.Body);
                _mediaManager.MediaSession!.SetRemoteDescription(SIPSorcery.SIP.App.SdpType.offer, remoteSdp);
                _logger.LogDebug("Set remote SDP from INVITE for Early Media");

                var sdpAnswer = _mediaManager.MediaSession!.CreateAnswer(null);
                
                var sessionProgressResponse = SIPResponse.GetResponse(
                    sipRequest,
                    SIPResponseStatusCodesEnum.SessionProgress,
                    null
                );
                
                sessionProgressResponse.Body = sdpAnswer.ToString();
                sessionProgressResponse.Header.ContentType = SDP.SDP_MIME_CONTENTTYPE;
                
                await m_pendingIncomingCall.ClientTransaction.SendProvisionalResponse(sessionProgressResponse);
                
                await _mediaManager.MediaSession.Start();
                
                _logger.LogInformation("Sent 183 Session Progress with SDP for Early Media");
                StatusMessage?.Invoke(this, "Early Media session established");
                
                return true;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to send send session progress");
                return false;
            }
        }

        public async Task<bool> AnswerAsync() {
            if (m_pendingIncomingCall == null) {
                StatusMessage?.Invoke(this, $"There was no pending call available to answer.");
                return false;
            }

            var sipRequest = m_pendingIncomingCall.ClientTransaction.TransactionRequest;
            
            _logger.LogDebug($"*** ANSWERING CALL *** CallId: {sipRequest.Header.CallId}");
            _logger.LogDebug($"Original INVITE From: {sipRequest.Header.From}");
            _logger.LogDebug($"Original INVITE To: {sipRequest.Header.To}");

            _logger.LogDebug($"INVITE Via: {sipRequest.Header.Vias.ToString()}");
            _logger.LogDebug($"INVITE Contact: {string.Join(",", sipRequest.Header.Contact.Select(x=>x.ToString()))}");
            _logger.LogDebug($"INVITE ReceivedFrom: {sipRequest.RemoteSIPEndPoint}");

            EnsureMediaSessionInitialized();
            
            _mediaManager!.InitializePeerConnection();
            _mediaManager.InitializeMediaSession();

            m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
            m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

            IPAddress? publicIp = null;
            if(!string.IsNullOrEmpty(_webRTCSettings?.PublicIP)) publicIp = IPAddress.Parse(_webRTCSettings.PublicIP);
            bool result = await m_userAgent.Answer(m_pendingIncomingCall, _mediaManager.MediaSession, publicIp);
            
            if (!result) {
                _logger.LogError($"*** ANSWER FAILED *** CallId: {sipRequest.Header.CallId}");
            } else {
                await _mediaManager.MediaSession!.Start();
                _logger.LogDebug($"*** CALL ANSWERED *** CallId: {sipRequest.Header.CallId}");
            }
            
            m_pendingIncomingCall = null;

            return result;
        }

        public void Redirect(string destination) {
            m_pendingIncomingCall?.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        public void AddIceCandidate(RTCIceCandidateInit candidate) {
            EnsureMediaSessionInitialized();
            if (_mediaManager != null) {
                _mediaManager.InitializePeerConnection();
                _mediaManager.AddIceCandidate(candidate);
            }
        }

        public void SetRemoteDescription(RTCSessionDescriptionInit description) {
            EnsureMediaSessionInitialized();
            _mediaManager!.InitializePeerConnection();
            _mediaManager.SetWebRtcRemoteDescription(description);
        }

        public async Task<RTCSessionDescriptionInit> CreateOfferAsync() {
            EnsureMediaSessionInitialized();
            _mediaManager!.InitializePeerConnection();
            _mediaManager.InitializeMediaSession();
            return await _mediaManager.CreateOfferAsync();
        }

        public async Task<RTCSessionDescriptionInit?> OfferAsync(RTCSessionDescriptionInit sdpOffer) {
            try {
                EnsureMediaSessionInitialized();
                _mediaManager!.InitializePeerConnection();
                _mediaManager.InitializeMediaSession();
                _mediaManager.SetWebRtcRemoteDescription(sdpOffer);
                return await _mediaManager.CreateAnswerAsync(); // This will trigger SdpAnswerGenerated event
            } catch (Exception ex) {
                _logger.LogError(ex, "Error in OfferAsync");
                return null;
            }
        }

        public void Reject() {
            m_pendingIncomingCall?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        public void Hangup() {
            if (m_userAgent.IsCallActive) {
                try {
                    _localHangupInitiated = true;
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
                _mediaManager?.Cancel();
                AudioStopped?.Invoke(this);
            } catch (Exception ex) {
                _logger.LogError($"Error stopping audio streams: {ex.Message}");
            }
        }

        public void ReleaseMediaResources() {
            _mediaManager?.Cancel();
            ResourcesReleased?.Invoke(this);
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

        public bool IsSecureContextReady() => _mediaManager?.PeerConnection?.IsSecureContextReady() == true;

        public void Shutdown() {
            _cts.Cancel();
            Hangup();
            _mediaManager?.Cancel();
            UnregisterFromNetworkMonitoring();
        }

        private void OnCallTrying(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            StatusMessage?.Invoke(this, "Call trying: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");
            CallTrying?.Invoke(this);

            if (m_userAgent.Dialogue != null && !string.IsNullOrEmpty(m_userAgent.Dialogue.CallId)) {
                CallInitiated?.Invoke(this, m_userAgent.Dialogue.CallId);
            }
        }

        private void OnCallRinging(ISIPClientUserAgent uac, SIPResponse sipResponse) {
            StatusMessage?.Invoke(this, "Call ringing: " + sipResponse.StatusCode + " " + sipResponse.ReasonPhrase + ".");

            if (!string.IsNullOrEmpty(sipResponse.Body)) {
                if (_lastRemoteSdp == sipResponse.Body) {
                    _logger.LogDebug("Same SDP already processed, skipping duplicate processing in ringing response");
                    return;
                }

                var remoteAnswer = new RTCSessionDescriptionInit {
                    type = RTCSdpType.answer,
                    sdp = sipResponse.Body
                };
                _mediaManager?.SetSipRemoteDescription(remoteAnswer);
                _lastRemoteSdp = sipResponse.Body;
                _logger.LogDebug("Processed new SDP in ringing response");
            }
            
            CallRinging?.Invoke(this);
        }

        private void OnCallFailed(ISIPClientUserAgent uac, string errorMessage, SIPResponse failureResponse) {
            StatusMessage?.Invoke(this, "Call failed: " + errorMessage + ".");
            CallFinished(null);
            Shutdown();
        }

        private void OnCallAnswered(ISIPClientUserAgent uac, SIPResponse response) {
            StatusMessage?.Invoke(this, "Call answered: " + response.StatusCode + " " + response.ReasonPhrase + ".");

            if (!string.IsNullOrEmpty(response.Body)) {
                if (_lastRemoteSdp == response.Body) {
                    _logger.LogDebug("Same SDP already processed, skipping duplicate processing in call answered");
                } else {
                    var remoteAnswer = new RTCSessionDescriptionInit {
                        type = RTCSdpType.answer,
                        sdp = response.Body
                    };
                    _mediaManager!.SetSipRemoteDescription(remoteAnswer);
                    _lastRemoteSdp = response.Body;
                    _logger.LogDebug("Processed new SDP in call answered response");
                }
            }

            CallAnswered?.Invoke(this);
        }

        private void CallFinished(SIPDialogue? dialogue) {
            try {
                var context = CreateHangupEventContext(dialogue);
                
                m_pendingIncomingCall = null;
                
                if (_mediaManager != null) {
                    try {
                        _mediaManager.Dispose();
                        _logger.LogDebug("MediaSessionManager disposed after call finished");
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error disposing MediaSessionManager");
                    } finally {
                        _mediaManager = null;
                    }
                }
                
                _lastRemoteSdp = null;
                _localHangupInitiated = false;
                StatusMessage?.Invoke(this, "Call finished and MediaSessionManager disposed.");
                
                CallFinishedWithContext?.Invoke(this, context);
                
            } catch (Exception ex) {
                _logger.LogError($"Error in CallFinished : {ex.Message}");
                StatusMessage?.Invoke(this, $"Error during call cleanup: {ex.Message}");
            } finally {
                try {
                    CallEnded?.Invoke(this);
                    _logger.LogDebug($"CallEnded event triggered ");
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

        private void RegisterWithNetworkMonitoring() {
            try {
                if (_networkMonitoringService != null) {
                    _networkMonitoringService.RegisterSipClient(_clientId, this);
                    _logger.LogDebug("SIP client {ClientId} registered with network monitoring service", _clientId);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to register SIP client {ClientId} with network monitoring service: {Message}",
                    _clientId, ex.Message);
            }
        }

        private void UnregisterFromNetworkMonitoring() {
            try {
                if (_networkMonitoringService != null) {
                    _networkMonitoringService.UnregisterSipClient(_clientId);
                    _logger.LogDebug("SIP client {ClientId} unregistered from network monitoring service", _clientId);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to unregister SIP client {ClientId} from network monitoring service: {Message}",
                    _clientId, ex.Message);
            }
        }

        public string GetClientId() {
            return _clientId;
        }

        public MediaSessionManager? MediaSessionManager => _mediaManager;
        public bool EnableWebRtcBridging => _enableWebRtcBridging;

        private HangupEventContext CreateHangupEventContext(SIPDialogue? dialogue) {
            var context = new HangupEventContext {
                Initiator = _localHangupInitiated ? HangupInitiator.Local : HangupInitiator.Remote,
                Reason = _localHangupInitiated ? "Local user hangup" : "Remote party hangup"
            };

            return context;
        }

        private void EnsureMediaSessionInitialized() {
            if (_mediaManager == null) {
                _mediaManager = new MediaSessionManager(_logger, _enableWebRtcBridging, _webRTCSettings);
                SetupMediaSessionEvents();
                _logger.LogDebug($"Created new MediaSessionManager for call (WebRTC bridging: {_enableWebRtcBridging})");
            }
        }

        private void SetupMediaSessionEvents() {
            _mediaManager!.SdpOfferGenerated += OnSdpOfferGenerated;
            _mediaManager!.SdpAnswerGenerated += OnSdpAnswerGenerated;
            _mediaManager!.IceCandidateGenerated += OnIceCandidateGenerated;
            _mediaManager!.ConnectionStateChanged += OnConnectionStateChanged;
        }

        private void OnSdpOfferGenerated(RTCSessionDescriptionInit offer) {
            try {
                _logger.LogDebug("SDP Offer generated by MediaSessionManager, handling SIP-specific logic");                
                StatusMessage?.Invoke(this, $"SDP Offer generated and ready for SIP transmission");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling SDP offer generated event");
            }
        }

        private void OnSdpAnswerGenerated(RTCSessionDescriptionInit answer) {
            try {
                _logger.LogDebug("SDP Answer generated by MediaSessionManager, handling SIP-specific logic");                
                StatusMessage?.Invoke(this, $"SDP Answer generated and ready for SIP transmission");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling SDP answer generated event");
            }
        }

        private void OnIceCandidateGenerated(RTCIceCandidateInit candidate) {
            try {
                _logger.LogDebug($"ICE candidate generated by MediaSessionManager: {candidate.candidate}");                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling ICE candidate generated event");
            }
        }

        private void OnConnectionStateChanged(RTCPeerConnectionState state) {
            try {
                _logger.LogDebug($"MediaSession connection state changed to: {state}");
                StatusMessage?.Invoke(this, $"Media connection state: {state}");

                switch (state) {
                    case RTCPeerConnectionState.connected:
                        _logger.LogDebug("Media connection established successfully");
                        break;
                    case RTCPeerConnectionState.disconnected:
                    case RTCPeerConnectionState.failed:
                        _logger.LogWarning($"Media connection issue: {state}");
                        break;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error handling connection state change event");
            }
        }
    }
}
        