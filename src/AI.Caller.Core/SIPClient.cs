using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System.Diagnostics;
using System.Net;
using System.Text.RegularExpressions;
using AI.Caller.Core.Recording;
using System.Threading.Tasks;

namespace AI.Caller.Core {
    public class SIPClient {
        private static string _sdpMimeContentType = SDP.SDP_MIME_CONTENTTYPE;
        private static int TRANSFER_RESPONSE_TIMEOUT_SECONDS = 10;

        private readonly ILogger _logger;
        private readonly SIPUserAgent m_userAgent;        
        private readonly SIPTransport m_sipTransport;
        private readonly SIPClientOptions _options;
        private readonly WebRTCSettings? _webRTCSettings;
        
        // 录音相关
        private IAudioRecordingManager? _recordingManager;
        private RecordingOptions? _recordingOptions;
        private bool _autoRecordingEnabled = false;

        public event Action<SIPClient>? CallAnswer;
        public event Action<SIPClient>? CallEnded;
        public event Action<SIPClient>? RemotePutOnHold;
        public event Action<SIPClient>? RemoteTookOffHold;
        public event Action<SIPClient, string>? StatusMessage;
        
        // 新增挂断相关事件
        public event Action<SIPClient>? HangupInitiated;
        public event Action<SIPClient>? AudioStopped;
        public event Action<SIPClient>? ResourcesReleased;
        
        // 网络状态相关事件
        public event Action<SIPClient>? NetworkDisconnected;
        public event Action<SIPClient>? NetworkReconnected;
        
        // 录音相关事件
        public event Action<SIPClient, string>? RecordingStarted;
        public event Action<SIPClient, string>? RecordingStopped;
        public event Action<SIPClient, RecordingErrorEventArgs>? RecordingError;

        public SIPDialogue Dialogue => m_userAgent.Dialogue;

        public bool IsCallActive    => m_userAgent.IsCallActive;
                                    
        public bool IsOnHold        => m_userAgent.IsOnLocalHold || m_userAgent.IsOnRemoteHold;

        public RTPSession?        MediaSession      { get; private set; }
        public RTCPeerConnection? RTCPeerConnection { get; private set; }
        
        // 录音相关属性
        public bool IsRecordingEnabled => _recordingManager != null;
        public bool IsRecording => _recordingManager?.IsRecording ?? false;
        public RecordingStatus? RecordingStatus => _recordingManager?.CurrentStatus;

        private SIPServerUserAgent? m_pendingIncomingCall;
        private CancellationTokenSource _cts = new CancellationTokenSource();
        
        // 网络监控相关
        private Timer? _networkMonitorTimer;
        private bool _isNetworkConnected = true;
        private DateTime _lastNetworkCheck = DateTime.UtcNow;
        private readonly object _networkStateLock = new object();

        public SIPClient(ILogger logger, SIPClientOptions options, SIPTransport sipTransport, WebRTCSettings? webRTCSettings = null) {
            _logger         = logger;
            _options        = options;
            m_sipTransport  = sipTransport;
            _webRTCSettings = webRTCSettings;
            m_userAgent = new SIPUserAgent(m_sipTransport, null);

            m_userAgent.ClientCallFailed += CallFailed;
            m_userAgent.ClientCallTrying += CallTrying;
            m_userAgent.ClientCallRinging += CallRinging;
            m_userAgent.ClientCallAnswered += CallAnswered;

            m_userAgent.ServerCallCancelled += IncomingCallCancelled;

            m_userAgent.OnDtmfTone += OnDtmfTone;
            m_userAgent.OnCallHungup += CallFinished;
            m_userAgent.OnTransferNotify += OnTransferNotify;
            
            // 启动网络监控
            StartNetworkMonitoring();
        }

        public async Task CallAsync(string destination) {
            SIPURI? callURI = null;
            string? sipUsername = null;
            string? sipPassword = null;
            string? fromHeader = null;

            if (destination.Contains("@") || _options.SIPServer == null) {
                callURI = SIPURI.ParseSIPURIRelaxed(destination);
                fromHeader = (new SIPFromHeader(_options.SIPFromName, new SIPURI(_options.SIPUsername, _options.SIPServer, null), null)).ToString();
            } else {
                callURI = SIPURI.ParseSIPURIRelaxed(destination + "@" + _options.SIPServer);
                sipUsername = _options.SIPUsername;
                sipPassword = _options.SIPPassword;
                fromHeader = (new SIPFromHeader(_options.SIPFromName, new SIPURI(_options.SIPUsername, _options.SIPServer, null), null)).ToString();
            }

            StatusMessage?.Invoke(this, $"Starting call to {callURI}.");

            var dstEndpoint = await SIPDns.ResolveAsync(callURI, false, _cts.Token);

            if (dstEndpoint == null) {
                StatusMessage?.Invoke(this, $"Call failed, could not resolve {callURI}.");
            } else {
                StatusMessage?.Invoke(this, $"Call progressing, resolved {callURI} to {dstEndpoint}.");
                Debug.WriteLine($"DNS lookup result for {callURI}: {dstEndpoint}.");
                SIPCallDescriptor callDescriptor = new SIPCallDescriptor(sipUsername, sipPassword, callURI.ToString(), fromHeader, null, null, null, null, SIPCallDirection.Out, _sdpMimeContentType, null, null);

                MediaSession = await CreateMediaSession();
                MediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                await m_userAgent.InitiateCallAsync(callDescriptor, MediaSession);
            }
        }

        private void OnForwardMediaToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType == SDPMediaTypesEnum.audio && MediaSession != null && m_userAgent.IsCallActive) {
                    MediaSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                    
                    // 传递音频数据给录音管理器（WebRTC传出音频）
                    if (_recordingManager != null && IsRecording) {
                        ForwardAudioToRecording(remote, mediaType, rtpPacket, AudioSource.WebRTC_Outgoing);
                    }
                }
            } catch (Exception ex) {
                _logger.LogError($"Error forwarding media to SIP: {ex.Message}");
                // Don't rethrow - we want to continue processing other packets
            }
        }

        private void OnRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (RTCPeerConnection != null && mediaType == SDPMediaTypesEnum.audio && RTCPeerConnection.connectionState == RTCPeerConnectionState.connected) {
                    RTCPeerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                }
                
                // 传递音频数据给录音管理器（RTP传入音频）
                if (_recordingManager != null && IsRecording && mediaType == SDPMediaTypesEnum.audio) {
                    ForwardAudioToRecording(remote, mediaType, rtpPacket, AudioSource.RTP_Incoming);
                }
            } catch (Exception ex) {
                _logger.LogError($"Error sending audio packet: {ex.Message}");
                // Don't rethrow - we want to continue processing other packets
            }
        }

        public void Cancel() {
            StatusMessage?.Invoke(this, "Cancelling SIP call to " + m_userAgent.CallDescriptor?.Uri + ".");
            m_userAgent.Cancel();
        }

        public void Accept(SIPRequest sipRequest) {
            m_pendingIncomingCall = m_userAgent.AcceptCall(sipRequest);
        }

        /// <summary>
        /// 接听
        /// </summary>
        /// <returns></returns>
        public async Task<bool> AnswerAsync() {
            if (m_pendingIncomingCall == null) {
                StatusMessage?.Invoke(this, $"There was no pending call available to answer.");
                return false;
            } else {
                var sipRequest = m_pendingIncomingCall.ClientTransaction.TransactionRequest;

                // Assume that if the INVITE request does not contain an SDP offer that it will be an 
                // audio only call.
                bool hasAudio = true;
                bool hasVideo = false;

                if (sipRequest.Body != null) {
                    SDP offerSDP = SDP.ParseSDPDescription(sipRequest.Body);
                    hasAudio = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.audio && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                    hasVideo = offerSDP.Media.Any(x => x.Media == SDPMediaTypesEnum.video && x.MediaStreamStatus != MediaStreamStatusEnum.Inactive);
                }

                MediaSession = await CreateMediaSession();
                MediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

                m_userAgent.RemotePutOnHold += OnRemotePutOnHold;
                m_userAgent.RemoteTookOffHold += OnRemoteTookOffHold;

                bool result = await m_userAgent.Answer(m_pendingIncomingCall, MediaSession);
                m_pendingIncomingCall = null;

                return result;
            }
        }

        /// <summary>
        /// 转接听指定的SIP地址。
        /// </summary>
        /// <param name="destination"></param>
        public void Redirect(string destination) {
            m_pendingIncomingCall?.Redirect(SIPResponseStatusCodesEnum.MovedTemporarily, SIPURI.ParseSIPURIRelaxed(destination));
        }

        /// <summary>
        /// 注册
        /// </summary>
        public void Register() {
            //var tcs = new TaskCompletionSource<bool>();

            var sipRegistrationClient = new SIPRegistrationUserAgent(
                m_sipTransport,
                _options.SIPUsername,
                _options.SIPPassword,
                _options.SIPServer,
                180);

            sipRegistrationClient.RegistrationSuccessful += (uac, resp) => {
                StatusMessage?.Invoke(this, $"SIP registration successful for {_options.SIPUsername}.");
                //tcs.TrySetResult(true);
            };

            sipRegistrationClient.RegistrationFailed += (uri, resp, err) => {
                StatusMessage?.Invoke(this, $"SIP registration failed for {_options.SIPUsername} with {err}.");
                //tcs.TrySetResult(false);
            };

            sipRegistrationClient.Start();
            //return tcs.Task;
        }

        //public async Task PutOnHoldAsync() {
        //    await MediaSession.PutOnHold();
        //    m_userAgent.PutOnHold();
        //    StatusMessage?.Invoke(this, "Local party put on hold");
        //}

        //public void TakeOffHold() {
        //    MediaSession.TakeOffHold();
        //    m_userAgent.TakeOffHold();
        //    StatusMessage?.Invoke(this, "Local party taken off on hold");
        //}
        
        
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
                //await RTCPeerConnection.Start();

                var answerSdp = RTCPeerConnection.createAnswer(new RTCAnswerOptions { });

                await RTCPeerConnection.setLocalDescription(answerSdp);
                return answerSdp;
            }
            return null;
        }

        /// <summary>
        /// 拒接
        /// </summary>
        public void Reject() {
            m_pendingIncomingCall?.Reject(SIPResponseStatusCodesEnum.BusyHere, null, null);
        }

        /// <summary>
        /// 挂断
        /// </summary>
        public void Hangup() {
            if (m_userAgent.IsCallActive) {
                try {
                    // 触发挂断开始事件
                    HangupInitiated?.Invoke(this);
                    StatusMessage?.Invoke(this, "Hangup initiated.");
                    
                    // 立即停止音频流
                    StopAudioStreams();
                    
                    // 执行SIP挂断
                    m_userAgent.Hangup();
                } catch (Exception ex) {
                    _logger.LogError($"Error in Hangup: {ex.Message}");
                } finally {
                    CallFinished(null);
                }
            }
        }

        /// <summary>
        /// 立即停止音频流
        /// </summary>
        public void StopAudioStreams() {
            try {
                // 停止RTP音频流
                if (MediaSession != null) {
                    // 移除音频包接收事件处理
                    MediaSession.OnRtpPacketReceived -= OnRtpPacketReceived;
                    StatusMessage?.Invoke(this, "Audio streams stopped.");
                }
                
                // 停止WebRTC音频流
                if (RTCPeerConnection != null) {
                    // 移除RTP包接收事件处理
                    RTCPeerConnection.OnRtpPacketReceived -= OnForwardMediaToSIP;
                }
                
                // 触发音频停止事件
                AudioStopped?.Invoke(this);
            } catch (Exception ex) {
                _logger.LogError($"Error stopping audio streams: {ex.Message}");
            }
        }

        /// <summary>
        /// 释放媒体资源
        /// </summary>
        public void ReleaseMediaResources() {
            bool rtcConnectionClosed = false;
            bool mediaSessionClosed = false;
            
            // 安全关闭RTCPeerConnection
            if (RTCPeerConnection != null) {
                try {
                    RTCPeerConnection.close();
                    RTCPeerConnection = null;
                    rtcConnectionClosed = true;
                    StatusMessage?.Invoke(this, "RTCPeerConnection resources released successfully.");
                    _logger.LogInformation($"RTCPeerConnection closed successfully for user {_options.SIPUsername}");
                } catch (Exception ex) {
                    _logger.LogError($"Error closing RTCPeerConnection for user {_options.SIPUsername}: {ex.Message}");
                    StatusMessage?.Invoke(this, $"Warning: RTCPeerConnection close failed: {ex.Message}");
                    // 即使关闭失败，也将引用设为null以防止内存泄漏
                    RTCPeerConnection = null;
                }
            }
            
            // 安全关闭MediaSession
            if (MediaSession != null) {
                try {
                    MediaSession.Close("Resources released");
                    MediaSession = null;
                    mediaSessionClosed = true;
                    StatusMessage?.Invoke(this, "MediaSession resources released successfully.");
                    _logger.LogInformation($"MediaSession closed successfully for user {_options.SIPUsername}");
                } catch (Exception ex) {
                    _logger.LogError($"Error closing MediaSession for user {_options.SIPUsername}: {ex.Message}");
                    StatusMessage?.Invoke(this, $"Warning: MediaSession close failed: {ex.Message}");
                    // 即使关闭失败，也将引用设为null以防止内存泄漏
                    MediaSession = null;
                }
            }
            
            // 无论是否有异常，都触发资源释放事件（如果有任何资源被处理）
            if (rtcConnectionClosed || mediaSessionClosed || RTCPeerConnection == null || MediaSession == null) {
                try {
                    ResourcesReleased?.Invoke(this);
                    _logger.LogInformation($"ResourcesReleased event triggered for user {_options.SIPUsername}");
                } catch (Exception ex) {
                    _logger.LogError($"Error triggering ResourcesReleased event for user {_options.SIPUsername}: {ex.Message}");
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

        /// <summary>
        /// 关闭
        /// </summary>
        public void Shutdown() {
            // 停止网络监控
            StopNetworkMonitoring();
            
            // 取消所有操作
            _cts.Cancel();
            
            Hangup();
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
            
            if (_webRTCSettings != null && _webRTCSettings.IceServers != null && _webRTCSettings.IceServers.Count > 0)
            {
                try
                {
                    iceServers.AddRange(_webRTCSettings.GetRTCIceServers());
                    _logger.LogInformation($"Using {iceServers.Count} configured ICE servers for WebRTC");
                }
                catch (ArgumentException ex)
                {
                    _logger.LogError(ex, "Failed to convert WebRTC settings to RTCIceServer instances. Using default STUN server.");
                }
            }
            
            if (iceServers.Count == 0)
            {
                _logger.LogWarning("No ICE servers configured, using default STUN server");
                //iceServers.Add(new RTCIceServer { urls = "stun:stun.sipsorcery.com" });
            }
            
            var iceTransportPolicy = RTCIceTransportPolicy.all;
            if (_webRTCSettings != null && 
                !string.IsNullOrEmpty(_webRTCSettings.IceTransportPolicy) && 
                _webRTCSettings.IceTransportPolicy.Equals("relay", StringComparison.OrdinalIgnoreCase))
            {
                iceTransportPolicy = RTCIceTransportPolicy.relay;
                _logger.LogInformation("Using 'relay' ICE transport policy");
            }
            
            return new RTCConfiguration
            {
                iceServers = iceServers.ToList(),
                iceTransportPolicy = iceTransportPolicy,
                X_DisableExtendedMasterSecretKey = true
            };
        }

        private void CreatePeerConnection() {
            var pcConfiguration = GetRTCConfiguration();
            var peerConnection = new RTCPeerConnection(pcConfiguration);
            
            // Set up event handlers and tracks
            SetupPeerConnectionEvents(peerConnection);

            RTCPeerConnection = peerConnection;
        }
        
        /// <summary>
        /// Sets up event handlers for a peer connection
        /// </summary>
        /// <param name="peerConnection">The RTCPeerConnection to set up events for</param>
        private void SetupPeerConnectionEvents(RTCPeerConnection peerConnection)
        {
            if (peerConnection == null)
                return;
                
            // Add the audio track
            MediaStreamTrack track = new MediaStreamTrack(
                SDPMediaTypesEnum.audio,
                false,
                [
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                    new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                ]
            );
            peerConnection.addTrack(track);
            
            // Set up event handlers
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
                if (candidate != null)
                {
                    _logger.LogDebug($"ICE candidate: {candidate.candidate}, Type: {candidate.type}");
                    
                    // Log when TURN relay is being used
                    if (candidate.candidate != null && candidate.candidate.Contains("typ relay"))
                    {
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
            
            // 自动开始录音（如果启用）
            if (_autoRecordingEnabled && _recordingManager != null && _recordingOptions != null)
            {
                _ = Task.Run(async () => await StartRecordingAsync());
            }
            
            CallAnswer?.Invoke(this);
        }

        private void CallFinished(SIPDialogue? dialogue) {
            try {
                _logger.LogInformation($"CallFinished triggered for user {_options.SIPUsername}");
                m_pendingIncomingCall = null;
                
                // 自动停止录音（如果正在录音）
                if (IsRecording)
                {
                    _ = Task.Run(async () => await StopRecordingAsync());
                }
                
                // 使用统一的资源释放方法
                ReleaseMediaResources();
                
                StatusMessage?.Invoke(this, "Call finished and resources cleaned up.");
            } catch (Exception ex) {
                _logger.LogError($"Error in CallFinished for user {_options.SIPUsername}: {ex.Message}");
                StatusMessage?.Invoke(this, $"Error during call cleanup: {ex.Message}");
            } finally {
                // 确保无论是否有异常都触发CallEnded事件
                try {
                    CallEnded?.Invoke(this);
                    _logger.LogInformation($"CallEnded event triggered for user {_options.SIPUsername}");
                } catch (Exception ex) {
                    _logger.LogError($"Error triggering CallEnded event for user {_options.SIPUsername}: {ex.Message}");
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

        /// <summary>
        /// 启动网络监控
        /// </summary>
        private void StartNetworkMonitoring() {
            try {
                // 每5秒检查一次网络状态
                _networkMonitorTimer = new Timer(CheckNetworkStatus, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
                _logger.LogInformation($"Network monitoring started for user {_options.SIPUsername}");
            } catch (Exception ex) {
                _logger.LogError($"Error starting network monitoring for user {_options.SIPUsername}: {ex.Message}");
            }
        }

        /// <summary>
        /// 停止网络监控
        /// </summary>
        private void StopNetworkMonitoring() {
            try {
                _networkMonitorTimer?.Dispose();
                _networkMonitorTimer = null;
                _logger.LogInformation($"Network monitoring stopped for user {_options.SIPUsername}");
            } catch (Exception ex) {
                _logger.LogError($"Error stopping network monitoring for user {_options.SIPUsername}: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查网络状态
        /// </summary>
        private void CheckNetworkStatus(object? state) {
            try {
                bool currentNetworkState = IsNetworkAvailable();
                
                lock (_networkStateLock) {
                    if (_isNetworkConnected && !currentNetworkState) {
                        // 网络从连接变为断开
                        _isNetworkConnected = false;
                        _logger.LogInformation($"Network disconnected detected for user {_options.SIPUsername}");
                        StatusMessage?.Invoke(this, "Network connection lost.");
                        
                        // 触发网络断开事件
                        NetworkDisconnected?.Invoke(this);
                        
                        // 执行本地挂断处理
                        HandleNetworkDisconnection();
                        
                    } else if (!_isNetworkConnected && currentNetworkState) {
                        // 网络从断开变为连接
                        _isNetworkConnected = true;
                        _logger.LogInformation($"Network reconnected detected for user {_options.SIPUsername}");
                        StatusMessage?.Invoke(this, "Network connection restored.");
                        
                        // 触发网络重连事件
                        NetworkReconnected?.Invoke(this);
                        
                        // 处理网络恢复
                        HandleNetworkReconnection();
                    }
                    
                    _lastNetworkCheck = DateTime.UtcNow;
                }
            } catch (Exception ex) {
                _logger.LogError($"Error checking network status for user {_options.SIPUsername}: {ex.Message}");
            }
        }

        /// <summary>
        /// 检查网络是否可用
        /// </summary>
        private bool IsNetworkAvailable() {
            try {
                // 检查SIP传输是否仍然有效
                if (m_sipTransport == null) {
                    return false;
                }

                // 检查是否有活动的通话且连接状态
                if (IsCallActive) {
                    // 检查RTCPeerConnection状态
                    if (RTCPeerConnection != null) {
                        var connectionState = RTCPeerConnection.connectionState;
                        if (connectionState == RTCPeerConnectionState.disconnected || 
                            connectionState == RTCPeerConnectionState.failed ||
                            connectionState == RTCPeerConnectionState.closed) {
                            return false;
                        }
                    }
                }

                return true;
            } catch (Exception ex) {
                _logger.LogError($"Error checking network availability for user {_options.SIPUsername}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 处理网络断开
        /// </summary>
        private void HandleNetworkDisconnection() {
            try {
                if (IsCallActive) {
                    _logger.LogInformation($"Handling network disconnection during active call for user {_options.SIPUsername}");
                    StatusMessage?.Invoke(this, "Network disconnected during call, performing local hangup.");
                    
                    // 执行本地挂断处理
                    PerformLocalHangup("Network disconnection");
                }
            } catch (Exception ex) {
                _logger.LogError($"Error handling network disconnection for user {_options.SIPUsername}: {ex.Message}");
            }
        }

        /// <summary>
        /// 处理网络重连
        /// </summary>
        private void HandleNetworkReconnection() {
            try {
                _logger.LogInformation($"Network reconnected for user {_options.SIPUsername}");
                // 网络恢复后的处理逻辑可以在这里添加
                // 例如：重新注册SIP账号、重发挂断通知等
            } catch (Exception ex) {
                _logger.LogError($"Error handling network reconnection for user {_options.SIPUsername}: {ex.Message}");
            }
        }

        /// <summary>
        /// 执行本地挂断处理
        /// </summary>
        private void PerformLocalHangup(string reason) {
            try {
                _logger.LogInformation($"Performing local hangup for user {_options.SIPUsername}, reason: {reason}");
                
                // 触发挂断开始事件
                HangupInitiated?.Invoke(this);
                StatusMessage?.Invoke(this, $"Local hangup initiated: {reason}");
                
                // 立即停止音频流
                StopAudioStreams();
                
                // 不调用m_userAgent.Hangup()，因为网络可能不可用
                // 直接进行本地资源清理
                CallFinished(null);

                _logger.LogInformation($"Local hangup completed for user {_options.SIPUsername}");
            } catch (Exception ex) {
                _logger.LogError($"Error performing local hangup for user {_options.SIPUsername}: {ex.Message}");
                // 即使出现异常，也要确保资源被清理
                try {
                    CallFinished(null);
                } catch (Exception cleanupEx) {
                    _logger.LogError($"Error in cleanup during local hangup for user {_options.SIPUsername}: {cleanupEx.Message}");
                }
            }
        }

        /// <summary>
        /// 获取网络连接状态
        /// </summary>
        public bool IsNetworkConnected {
            get {
                lock (_networkStateLock) {
                    return _isNetworkConnected;
                }
            }
        }

        #region 录音功能

        /// <summary>
        /// 设置录音管理器
        /// </summary>
        /// <param name="recordingManager">录音管理器实例</param>
        /// <param name="recordingOptions">录音选项</param>
        /// <param name="autoRecording">是否启用自动录音</param>
        public void SetRecordingManager(IAudioRecordingManager recordingManager, RecordingOptions recordingOptions, bool autoRecording = true)
        {
            _recordingManager = recordingManager ?? throw new ArgumentNullException(nameof(recordingManager));
            _recordingOptions = recordingOptions ?? throw new ArgumentNullException(nameof(recordingOptions));
            _autoRecordingEnabled = autoRecording;

            // 订阅录音管理器事件
            _recordingManager.StatusChanged += OnRecordingStatusChanged;
            _recordingManager.ErrorOccurred += OnRecordingErrorOccurred;

            _logger.LogInformation($"Recording manager set for SIPClient {_options.SIPUsername}, auto recording: {autoRecording}");
        }

        /// <summary>
        /// 手动开始录音
        /// </summary>
        /// <param name="options">录音选项，如果为null则使用默认选项</param>
        /// <returns>是否成功开始录音</returns>
        public async Task<bool> StartRecordingAsync(RecordingOptions? options = null)
        {
            if (_recordingManager == null)
            {
                _logger.LogWarning("Cannot start recording: recording manager not set");
                return false;
            }

            if (!IsCallActive)
            {
                _logger.LogWarning("Cannot start recording: no active call");
                return false;
            }

            try
            {
                var recordingOptions = options ?? _recordingOptions;
                if (recordingOptions == null)
                {
                    _logger.LogError("Cannot start recording: no recording options available");
                    return false;
                }

                var result = await _recordingManager.StartRecordingAsync(recordingOptions);
                if (result)
                {
                    _logger.LogInformation($"Recording started for call {Dialogue?.CallId}");
                    StatusMessage?.Invoke(this, "Recording started");
                }
                else
                {
                    _logger.LogWarning("Failed to start recording");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error starting recording");
                return false;
            }
        }

        /// <summary>
        /// 手动停止录音
        /// </summary>
        /// <returns>录音文件路径，如果失败返回null</returns>
        public async Task<string?> StopRecordingAsync()
        {
            if (_recordingManager == null)
            {
                _logger.LogWarning("Cannot stop recording: recording manager not set");
                return null;
            }

            if (!IsRecording)
            {
                _logger.LogWarning("Cannot stop recording: not currently recording");
                return null;
            }

            try
            {
                var filePath = await _recordingManager.StopRecordingAsync();
                if (filePath != null)
                {
                    _logger.LogInformation($"Recording stopped, file saved: {filePath}");
                    StatusMessage?.Invoke(this, $"Recording stopped, file saved: {Path.GetFileName(filePath)}");
                }
                else
                {
                    _logger.LogWarning("Recording stopped but no file path returned");
                }

                return filePath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping recording");
                return null;
            }
        }

        /// <summary>
        /// 暂停录音
        /// </summary>
        /// <returns>是否成功暂停</returns>
        public async Task<bool> PauseRecordingAsync()
        {
            if (_recordingManager == null)
            {
                _logger.LogWarning("Cannot pause recording: recording manager not set");
                return false;
            }

            try
            {
                var result = await _recordingManager.PauseRecordingAsync();
                if (result)
                {
                    _logger.LogInformation("Recording paused");
                    StatusMessage?.Invoke(this, "Recording paused");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error pausing recording");
                return false;
            }
        }

        /// <summary>
        /// 恢复录音
        /// </summary>
        /// <returns>是否成功恢复</returns>
        public async Task<bool> ResumeRecordingAsync()
        {
            if (_recordingManager == null)
            {
                _logger.LogWarning("Cannot resume recording: recording manager not set");
                return false;
            }

            try
            {
                var result = await _recordingManager.ResumeRecordingAsync();
                if (result)
                {
                    _logger.LogInformation("Recording resumed");
                    StatusMessage?.Invoke(this, "Recording resumed");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resuming recording");
                return false;
            }
        }

        /// <summary>
        /// 取消录音
        /// </summary>
        /// <returns>是否成功取消</returns>
        public async Task<bool> CancelRecordingAsync()
        {
            if (_recordingManager == null)
            {
                _logger.LogWarning("Cannot cancel recording: recording manager not set");
                return false;
            }

            try
            {
                var result = await _recordingManager.CancelRecordingAsync();
                if (result)
                {
                    _logger.LogInformation("Recording cancelled");
                    StatusMessage?.Invoke(this, "Recording cancelled");
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cancelling recording");
                return false;
            }
        }

        /// <summary>
        /// 处理录音状态变化事件
        /// </summary>
        private void OnRecordingStatusChanged(object? sender, RecordingStatusEventArgs e)
        {
            try
            {
                var status = e.Status;
                _logger.LogDebug($"Recording status changed: {status.State}");

                switch (status.State)
                {
                    case RecordingState.Recording:
                        RecordingStarted?.Invoke(this, status.CurrentFilePath ?? "Unknown");
                        break;
                    case RecordingState.Completed:
                        RecordingStopped?.Invoke(this, status.CurrentFilePath ?? "Unknown");
                        break;
                    case RecordingState.Error:
                        var errorArgs = new RecordingErrorEventArgs(RecordingErrorCode.Unknown, status.ErrorMessage ?? "Unknown error");
                        RecordingError?.Invoke(this, errorArgs);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling recording status change");
            }
        }

        /// <summary>
        /// 处理录音错误事件
        /// </summary>
        private void OnRecordingErrorOccurred(object? sender, RecordingErrorEventArgs e)
        {
            try
            {
                _logger.LogError($"Recording error occurred: {e.ErrorCode} - {e.ErrorMessage}");
                RecordingError?.Invoke(this, e);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling recording error event");
            }
        }

        /// <summary>
        /// 将音频数据转发给录音管理器
        /// </summary>
        private void ForwardAudioToRecording(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket, AudioSource audioSource)
        {
            try
            {
                if (_recordingManager == null || !IsRecording || mediaType != SDPMediaTypesEnum.audio)
                    return;

                // 创建音频帧
                var audioFormat = new Recording.AudioFormat(
                    sampleRate: 8000, // 默认SIP音频采样率
                    channels: 1,      // 单声道
                    bitsPerSample: 16, // 16位
                    sampleFormat: AudioSampleFormat.PCM
                );

                var audioFrame = new AudioFrame(
                    data: rtpPacket.Payload,
                    format: audioFormat,
                    source: audioSource
                );

                // 通过AudioRecorder传递音频数据
                // 注意：这里需要获取AudioRecorder实例，我们需要修改AudioRecordingManager来暴露这个接口
                // 或者直接在AudioRecordingManager中添加接收RTP包的方法
                if (_recordingManager is AudioRecordingManager recordingManager)
                {
                    // 直接调用内部方法处理音频数据
                    recordingManager.ProcessAudioFrame(audioFrame);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error forwarding audio to recording: {ex.Message}");
            }
        }

        #endregion
    }
}