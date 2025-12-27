using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System.Net;
using AI.Caller.Core.Media;
using AI.Caller.Core.Network;

namespace AI.Caller.Core {
    public class MediaSessionManager : IDisposable {
        private readonly object _lock = new object();
        private readonly ILogger _logger;
        private readonly bool _enableWebRtcBridging;
        private readonly WebRTCSettings? _webRTCSettings;        
        private readonly AudioCodecFactory? _codecFactory;
        private readonly object _codecLock = new object();
        private readonly CodecHealthMonitor? _codecHealthMonitor;
        private readonly TimeSpan _codecSwitchCooldown = TimeSpan.FromSeconds(30);
        
        private bool _disposed = false;
        private IAudioBridge? _audioBridge;
        private VoIPMediaSession? _voipSession;
        private RTCPeerConnection? _peerConnection;
        private CodecNegotiationResult? _currentCodecNegotiation;
        private DateTime _lastCodecSwitch = DateTime.MinValue;

        public event Action<RTCIceCandidateInit>? IceCandidateGenerated;
        public event Action<RTCSessionDescriptionInit>? SdpOfferGenerated;
        public event Action<RTCSessionDescriptionInit>? SdpAnswerGenerated;

        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? AudioDataSent;
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? AudioDataReceived;
        /// <summary>
        /// 再answer前订阅
        /// </summary>
        public event Action<AudioCodec, int, int>? MediaConfigurationChanged;
        public event Action<AudioCodec, AudioCodec, string>? CodecSwitched;
        public event Action<AudioCodec, string>? CodecSwitchFailed;
        public event Action<RTCPeerConnectionState>? ConnectionStateChanged;

        public IMediaSession? MediaSession => _voipSession;
        public RTCPeerConnection? PeerConnection => _peerConnection;

        public int SelectedSampleRate { get; private set; } = 8000;
        public int SelectedPayloadType { get; private set; } = 8;
        public AudioCodec SelectedCodec { get; private set; } = AudioCodec.PCMA;

        public MediaSessionManager(ILogger logger, bool enableWebRtcBridging = true, WebRTCSettings? webRTCSettings = null, AudioCodecFactory? codecFactory = null, CodecHealthMonitor? codecHealthMonitor = null) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _enableWebRtcBridging = enableWebRtcBridging;
            _webRTCSettings = webRTCSettings;
            _codecFactory = codecFactory;
            _codecHealthMonitor = codecHealthMonitor;
            
            if (_codecHealthMonitor != null) {
                _codecHealthMonitor.CodecHealthChanged += OnCodecHealthChanged;
            }
        }

        public void SetAudioBridge(IAudioBridge audioBridge) {
            _audioBridge = audioBridge;
            audioBridge.SetMediaSessionManager(this);            
            _logger.LogDebug("Audio bridge attached to MediaSessionManager");
        }

        public void SendAudioFrame(byte[] audioFrame) {
            if (_disposed || _voipSession == null || _voipSession.IsClosed) {
                _logger.LogWarning($"send audio data exception, _disposed=>{_disposed}, _voipSession is null=>{_voipSession==null}, _voipSession isClosed=>{_voipSession?.IsClosed}");
                return;
            }

            if (audioFrame != null && audioFrame.Length > 0) {                
                uint timestampIncrement = 160u;

                _voipSession.SendAudio(timestampIncrement, audioFrame);                
                if (_voipSession.AudioDestinationEndPoint != null) {
                    var rtpPacket = new RTPPacket {
                        Header = new RTPHeader {
                            Timestamp = (uint)DateTime.UtcNow.Ticks,
                            PayloadType = (byte)SelectedPayloadType
                        },
                        Payload = audioFrame
                    };
                    AudioDataSent?.Invoke(
                        _voipSession.AudioDestinationEndPoint, 
                        SDPMediaTypesEnum.audio, 
                        rtpPacket
                    );
                }
            }
        }

        public void InitializeMediaSession() {
            ThrowIfDisposed();

            VoIPMediaSession? tempVoipSession = null;

            lock (_lock) {
                if (_voipSession != null) {
                    _logger.LogDebug("VoIPMediaSession already initialized, skipping.");
                    return;
                }

                try {
                    tempVoipSession = new VoIPMediaSession(new MediaEndPoints() );
                    tempVoipSession.AcceptRtpFromAny = true;
                    
                    // 🔧 IMPROVED: 优先G722，智能排序
                    var audioTrack = new MediaStreamTrack(
                        SDPMediaTypesEnum.audio,
                        false,
                        [
                            //new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),  // 优先G722
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),  // 备选G711a
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)   // 备选G711μ
                        ]
                    );

                    tempVoipSession.addTrack(audioTrack);
                    tempVoipSession.OnRtpPacketReceived += OnRtpPacketReceived;

                    _voipSession = tempVoipSession;
                    tempVoipSession = null; // Prevent disposal in catch block

                    _logger.LogInformation("VoIPMediaSession initialized successfully with G722 priority.");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to initialize VoIPMediaSession");

                    tempVoipSession?.Dispose();
                    _voipSession = null;
                    throw;
                }
            }
        }

        public void InitializePeerConnection() {
            InitializePeerConnection(BuildRTCConfiguration());
        }

        public void InitializePeerConnection(RTCConfiguration config) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(config);

            lock (_lock) {
                if (_peerConnection != null) {
                    _logger.LogDebug("RTCPeerConnection already initialized, skipping.");
                    return;
                }

                RTCPeerConnection? tempPeerConnection = null;

                try {
                    tempPeerConnection = new RTCPeerConnection(config);
                    var audioTrack = new MediaStreamTrack(
                        SDPMediaTypesEnum.audio,
                        false,
                        [
                            //new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.G722),
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                        ]
                    );
                    tempPeerConnection.addTrack(audioTrack);
                    SetupPeerConnectionEvents(tempPeerConnection);
                    tempPeerConnection.OnRtpPacketReceived += OnForwardMediaToSIP;

                    _peerConnection = tempPeerConnection;
                    tempPeerConnection = null;

                    _logger.LogInformation("RTCPeerConnection initialized with {IceServerCount} ICE servers",
                        config.iceServers?.Count ?? 0);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to initialize RTCPeerConnection");

                    tempPeerConnection?.Dispose();
                    _peerConnection = null;
                    throw;
                }
            }
        }

        public void AddIceCandidate(RTCIceCandidateInit candidate) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(candidate);

            lock (_lock) {
                if (_peerConnection == null) {
                    _logger.LogWarning("Cannot add ICE candidate: RTCPeerConnection is null");
                    return;
                }

                try {
                    _peerConnection.addIceCandidate(candidate);
                    _logger.LogDebug($"Added ICE candidate: {candidate.candidate}");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to add ICE candidate");
                    throw;
                }
            }
        }

        public void SetWebRtcRemoteDescription(RTCSessionDescriptionInit description) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(description);

            lock (_lock) {
                if (_peerConnection == null) {
                    throw new InvalidOperationException("RTCPeerConnection is not initialized");
                }
            }

            try {
                var result = _peerConnection.setRemoteDescription(description);
                if (result != SetDescriptionResultEnum.OK) {
                    throw new InvalidOperationException($"Failed to set remote description on RTCPeerConnection: {result}");
                }
                _logger.LogDebug($"Set WebRTC remote description ({description.type}) successfully.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to set WebRTC remote description");
                throw;
            }
        }

        public void SetSipRemoteDescription(RTCSessionDescriptionInit description) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(description);

            if (_voipSession == null) {
                _logger.LogDebug("VoIPMediaSession not initialized, initializing now for SIP remote description.");
                InitializeMediaSession();
            }

            try {
                var sdp = SDP.ParseSDPDescription(description.sdp);
                _voipSession!.SetRemoteDescription(description.type == RTCSdpType.offer ? SdpType.offer : SdpType.answer, sdp);
                _logger.LogDebug($"Set SIP remote description ({description.type}) successfully on VoIPMediaSession.");

                // Parse SDP to determine negotiated codec
                ParseSdpAndUpdateCodecConfiguration(sdp);

                //if (sdp.Connection != null && sdp.Media != null) {
                //    var audioMedia = sdp.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);
                //    if (audioMedia != null && audioMedia.Port > 0) {
                //        var remoteEndPoint = new IPEndPoint(
                //            IPAddress.Parse(sdp.Connection.ConnectionAddress),
                //            audioMedia.Port
                //        );
                //        _voipSession.SetDestination(SDPMediaTypesEnum.audio, remoteEndPoint, remoteEndPoint);
                //        _logger.LogDebug($"Configured VoIPMediaSession destination to {remoteEndPoint} for audio.");
                //    } else {
                //        _logger.LogWarning("No valid audio media found in SDP, cannot set VoIPMediaSession destination.");
                //    }
                //} else {
                //    _logger.LogWarning("Invalid SDP: missing connection or media information.");
                //}
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to set SIP remote description");
                throw;
            }
        }

        public void Cancel() {
            if (_voipSession != null) {
                _voipSession.OnRtpPacketReceived -= OnRtpPacketReceived;
            }
            if (_peerConnection != null) {
                _peerConnection.OnRtpPacketReceived -= OnForwardMediaToSIP;
            }
        }

        public async Task<RTCSessionDescriptionInit> CreateOfferAsync() {
            ThrowIfDisposed();

            EnsurePeerConnectionInitialized();

            lock (_lock) {
                if (_peerConnection == null) {
                    throw new InvalidOperationException("RTCPeerConnection is not initialized");
                }
            }

            try {
                var sdpOffer = _peerConnection.createOffer();
                await _peerConnection.setLocalDescription(sdpOffer);
                _logger.LogDebug("Generated and set SDP Offer for RTCPeerConnection.");

                //lock (_lock) {
                //    if (_mediaSession != null) {
                //        var offerSDP = SDP.ParseSDPDescription(sdpOffer.sdp);
                //        _mediaSession.SetRemoteDescription(SdpType.offer, offerSDP);
                //        _logger.LogInformation("Set SDP Offer for RTPSession.");
                //    }
                //}

                SdpOfferGenerated?.Invoke(sdpOffer);
                return sdpOffer;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error generating SDP Offer");
                throw;
            }
        }

        public async Task<RTCSessionDescriptionInit> CreateAnswerAsync() {
            ThrowIfDisposed();

            EnsurePeerConnectionInitialized();

            lock (_lock) {
                if (_peerConnection == null) {
                    throw new InvalidOperationException("RTCPeerConnection is not initialized");
                }
            }

            try {
                var answerSdp = _peerConnection.createAnswer(new RTCAnswerOptions());
                await _peerConnection.setLocalDescription(answerSdp);
                _logger.LogDebug("Generated and set SDP Answer for RTCPeerConnection.");

                lock (_lock) {
                    if (_voipSession != null) {
                        var rtpAnswerSdp = SDP.ParseSDPDescription(answerSdp.sdp);
                        _voipSession.SetRemoteDescription(SdpType.answer, rtpAnswerSdp);
                        _logger.LogDebug("Set SDP Answer for VoIPMediaSession.");
                    }
                }

                SdpAnswerGenerated?.Invoke(answerSdp);
                return answerSdp;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error generating SDP Answer");
                throw;
            }
        }

        public async Task<RTCSessionDescriptionInit> InitiateBrowserCallAsync() {
            ThrowIfDisposed();
            try {
                var offer = await CreateOfferAsync();
                _logger.LogDebug("Browser call initiated successfully with CreateOffer->SetLocalDescription flow");
                return offer;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to initiate browser call");
                throw;
            }
        }

        public async Task<RTCSessionDescriptionInit> HandleBrowserIncomingCallAsync(RTCSessionDescriptionInit remoteOffer) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(remoteOffer);

            try {
                SetWebRtcRemoteDescription(remoteOffer);
                var answer = await CreateAnswerAsync();
                _logger.LogDebug("Browser incoming call handled successfully with SetRemoteDescription->CreateAnswer->SetLocalDescription flow");
                return answer;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to handle browser incoming call");
                throw;
            }
        }

        public async Task<bool> WaitForConnectionAsync(int timeoutMs = 10000) {
            if (_peerConnection == null) {
                return false;
            }

            if (_peerConnection.connectionState == RTCPeerConnectionState.connected) {
                return true;
            }

            var tcs = new TaskCompletionSource<bool>();
            var timeout = Task.Delay(timeoutMs);

            void OnStateChange(RTCPeerConnectionState state) {
                if (state == RTCPeerConnectionState.connected) {
                    tcs.TrySetResult(true);
                } else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.closed) {
                    tcs.TrySetResult(false);
                }
            }

            ConnectionStateChanged += OnStateChange;

            try {
                var completedTask = await Task.WhenAny(tcs.Task, timeout);
                if (completedTask == timeout) {
                    _logger.LogWarning("Timeout waiting for RTCPeerConnection to connect");
                    return false;
                }
                return await tcs.Task;
            } finally {
                ConnectionStateChanged -= OnStateChange;
            }
        }

        public void Dispose() {
            if (_disposed) return;

            lock (_lock) {
                if (_disposed) return;

                try {
                    SdpOfferGenerated = null;
                    SdpAnswerGenerated = null;
                    IceCandidateGenerated = null;
                    AudioDataSent = null;
                    AudioDataReceived = null;
                    ConnectionStateChanged = null;
                    MediaConfigurationChanged = null;
                    CodecSwitched = null;
                    CodecSwitchFailed = null;

                    // Unsubscribe from codec health monitor
                    if (_codecHealthMonitor != null) {
                        _codecHealthMonitor.CodecHealthChanged -= OnCodecHealthChanged;
                    }

                    if (_voipSession != null) {
                        try {
                            _voipSession.OnRtpPacketReceived -= OnRtpPacketReceived;
                            _voipSession.Dispose();
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error disposing VoIPMediaSession");
                        } finally {
                            _voipSession = null;
                        }
                    }

                    if (_peerConnection != null) {
                        try {
                            _peerConnection.OnRtpPacketReceived -= OnForwardMediaToSIP;
                            _peerConnection.Dispose();
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error disposing RTCPeerConnection");
                        } finally {
                            _peerConnection = null;
                        }
                    }

                    _logger.LogDebug("MediaSessionManager disposed successfully");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error during MediaSessionManager disposal");
                } finally {
                    _disposed = true;
                }
            }
        }


        private RTCConfiguration BuildRTCConfiguration() {
            var iceServers = new List<RTCIceServer>();

            if (_webRTCSettings != null && _webRTCSettings.IceServers != null && _webRTCSettings.IceServers.Count > 0) {
                try {
                    iceServers.AddRange(_webRTCSettings.GetRTCIceServers());
                    _logger.LogDebug($"Using {iceServers.Count} configured ICE servers for RTCPeerConnection");
                } catch (ArgumentException ex) {
                    _logger.LogError(ex, "Failed to convert WebRTC settings to RTCIceServer instances. Using default STUN server.");
                }
            }

            if (iceServers.Count == 0) {
                _logger.LogDebug("No ICE servers configured, using default STUN server");
                iceServers.Add(new RTCIceServer { urls = "stun:stun.l.google.com:19302" });
            }

            var iceTransportPolicy = RTCIceTransportPolicy.all;
            if (_webRTCSettings != null &&
                !string.IsNullOrEmpty(_webRTCSettings.IceTransportPolicy) &&
                _webRTCSettings.IceTransportPolicy.Equals("relay", StringComparison.OrdinalIgnoreCase)) {
                iceTransportPolicy = RTCIceTransportPolicy.relay;
                _logger.LogDebug("Using 'relay' ICE transport policy");
            }

            return new RTCConfiguration {
                iceServers = iceServers,
                iceTransportPolicy = iceTransportPolicy,
                X_DisableExtendedMasterSecretKey = true
            };
        }

        private void EnsurePeerConnectionInitialized() {
            if (!_enableWebRtcBridging) {
                _logger.LogDebug("WebRTC bridging disabled, skipping RTCPeerConnection initialization");
                return;
            }

            lock (_lock) {
                if (_peerConnection != null) {
                    return;
                }
            }
            if (_voipSession == null) {
                InitializeMediaSession();
            }

            InitializePeerConnection();
            _logger.LogDebug("RTCPeerConnection auto-initialized with configuration");
        }

        private void OnRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    _logger.LogDebug($"Skipping non-audio or empty RTP packet: MediaType={mediaType}, PayloadLength={(rtpPacket?.Payload?.Length ?? 0)}");
                    return;
                }

                if (_audioBridge != null) {
                    try {
                        int actualSampleRate = SelectedSampleRate;
                        _logger.LogTrace("🎵 Processing RTP audio: PayloadType={PayloadType}, SampleRate={SampleRate}, Size={Size} bytes", rtpPacket.Header.PayloadType, actualSampleRate, rtpPacket.Payload.Length);
                        _audioBridge.ProcessIncomingAudio(rtpPacket.Payload, actualSampleRate, rtpPacket.Header.PayloadType);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error processing audio through audio bridge");
                    }
                }

                if (_enableWebRtcBridging && _peerConnection != null) {
                    try {
                        _peerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                        _logger.LogTrace($"Forwarded RTP audio to WebRTC: {rtpPacket.Payload.Length} bytes from {remote}");
                        AudioDataReceived?.Invoke(remote, mediaType, rtpPacket);
                    } catch (Exception ex) {
                        _logger.LogError($"Error sending audio to RTCPeerConnection (DTLS may not be ready): {ex.Message}");
                    }
                } else if (!_enableWebRtcBridging) {
                    _logger.LogTrace("WebRTC bridging disabled, skipping WebRTC audio forwarding");
                    AudioDataReceived?.Invoke(remote, mediaType, rtpPacket);
                } else {
                    _logger.LogDebug("RTCPeerConnection not available, skipping WebRTC audio forwarding");
                }
            } catch (Exception ex) {
                _logger.LogError($"Error processing RTP audio packet from {remote}: {ex.Message}");
            }
        }

        private void OnForwardMediaToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    _logger.LogDebug($"Skipping non-audio or empty RTP packet: MediaType={mediaType}, PayloadLength={(rtpPacket?.Payload?.Length ?? 0)}");
                    return;
                }

                if (_enableWebRtcBridging && _voipSession != null && !_voipSession.IsClosed) {
                    var sendToEndPoint = _voipSession.AudioDestinationEndPoint;
                    if (sendToEndPoint != null) {
                        _voipSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                        _logger.LogTrace($"Forwarded WebRTC audio to SIP: {rtpPacket.Payload.Length} bytes from {remote} to {sendToEndPoint}");
                        AudioDataSent?.Invoke(remote, mediaType, rtpPacket);
                    }
                } else if (!_enableWebRtcBridging) {
                    _logger.LogTrace("WebRTC bridging disabled, skipping SIP audio forwarding");
                } else {
                    if (_voipSession == null) {
                        _logger.LogError("*** VOIP SESSION NULL *** Cannot forward audio to SIP - VoIPMediaSession not initialized");
                    } else if (_voipSession.IsClosed) {
                        _logger.LogWarning("*** VOIP SESSION CLOSED *** Cannot forward audio to SIP - VoIPMediaSession was closed");
                    }
                }
            } catch (Exception ex) {
                _logger.LogError($"Error forwarding media to SIP: {ex.Message}");
            }
        }

        private void SetupPeerConnectionEvents(RTCPeerConnection? peerConnection = null) {
            var pc = peerConnection ?? _peerConnection;
            if (pc == null) return;

            pc.onconnectionstatechange += (state) => {
                _logger.LogDebug($"RTCPeerConnection state changed to: {state}");
                if (state == RTCPeerConnectionState.connected) {
                    _logger.LogDebug("Peer connection connected - DTLS transport should be available now.");
                } else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected) {
                    _logger.LogWarning($"RTCPeerConnection failed or disconnected: {state} - DTLS transport may be unavailable");
                }
                ConnectionStateChanged?.Invoke(state);
            };

            pc.oniceconnectionstatechange += (state) => {
                _logger.LogDebug($"ICE connection state changed to: {state}");
                if (state == RTCIceConnectionState.connected) {
                    _logger.LogDebug("ICE connection established - preparing DTLS transport");
                } else if (state == RTCIceConnectionState.failed || state == RTCIceConnectionState.disconnected) {
                    _logger.LogWarning($"ICE connection issue: {state} - may affect DTLS transport");
                }
            };

            pc.onicegatheringstatechange += (state) => {
                _logger.LogDebug($"ICE gathering state changed to: {state}");
            };

            pc.onsignalingstatechange += () => {
                var state = pc.signalingState;
                _logger.LogDebug($"Signaling state changed to: {state}");
            };

            pc.onicecandidate += (candidate) => {
                if (candidate != null) {
                    _logger.LogDebug($"ICE candidate: {candidate.candidate}, Type: {candidate.type}, Address: {candidate.address}, Port: {candidate.port}");
                    if (candidate.candidate != null && candidate.candidate.Contains("typ relay")) {
                        _logger.LogDebug("Using TURN relay for media.");
                    }
                    var candidateInit = new RTCIceCandidateInit {
                        candidate = candidate.candidate,
                        sdpMid = candidate.sdpMid,
                        sdpMLineIndex = candidate.sdpMLineIndex
                    };
                    IceCandidateGenerated?.Invoke(candidateInit);
                }
            };
        }

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(MediaSessionManager));
            }
        }

        /// <summary>
        /// Handle network quality changes and adapt codec accordingly
        /// </summary>
        public async Task OnNetworkQualityChanged(NetworkQuality quality) {
            if (_codecFactory == null || _currentCodecNegotiation == null) {
                return;
            }

            try {
                _logger.LogInformation("🌐 Network quality changed to {Quality}, evaluating codec adaptation", quality);

                bool shouldSwitchCodec = false;
                AudioCodec targetCodec = _currentCodecNegotiation.SelectedCodec;

                if (quality <= NetworkQuality.Poor && _currentCodecNegotiation.SelectedCodec == AudioCodec.G722) {
                    targetCodec = _currentCodecNegotiation.FallbackCodec;
                    shouldSwitchCodec = true;
                    _logger.LogInformation("📉 Poor network quality detected, switching from G.722 to {FallbackCodec}", targetCodec);
                }
                else if (quality >= NetworkQuality.Good && 
                         _currentCodecNegotiation.IsUsingFallback && 
                         _currentCodecNegotiation.FallbackCodec == AudioCodec.G722) {
                    targetCodec = AudioCodec.G722;
                    shouldSwitchCodec = true;
                    _logger.LogInformation("📈 Network quality improved, attempting to switch back to G.722");
                }

                if (shouldSwitchCodec) {
                    await SwitchCodec(targetCodec, $"Network quality changed to {quality}");
                }

            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Error handling network quality change: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Switch to a different codec dynamically
        /// </summary>
        public async Task<bool> SwitchCodec(AudioCodec newCodec, string reason = "") {
            if (_codecFactory == null) {
                _logger.LogWarning("⚠️ Cannot switch codec: AudioCodecFactory not available");
                return false;
            }

            lock (_codecLock) {
                if (DateTime.UtcNow - _lastCodecSwitch < _codecSwitchCooldown) {
                    _logger.LogWarning("⏳ Codec switch blocked: cooldown period active (last switch: {LastSwitch})", _lastCodecSwitch);
                    return false;
                }

                if (SelectedCodec == newCodec) {
                    _logger.LogDebug("ℹ️ Already using codec {Codec}, no switch needed", newCodec);
                    return true;
                }
            }

            try {
                _logger.LogInformation("🔄 Switching codec from {CurrentCodec} to {NewCodec}: {Reason}", SelectedCodec, newCodec, reason);

                var previousCodec = SelectedCodec;

                if (!_codecFactory.IsCodecHealthy(newCodec)) {
                    _logger.LogWarning("⚠️ Target codec {Codec} is not healthy, aborting switch", newCodec);
                    CodecSwitchFailed?.Invoke(newCodec, "Target codec is not healthy");
                    return false;
                }

                if (!await TestCodecBeforeSwitch(newCodec)) {
                    _logger.LogWarning("⚠️ Codec test failed for {Codec}, aborting switch", newCodec);
                    CodecSwitchFailed?.Invoke(newCodec, "Codec test failed");
                    return false;
                }

                var newPayloadType = GetPayloadTypeForCodec(newCodec);
                var newSampleRate = GetSampleRateForCodec(newCodec);

                SelectedCodec = newCodec;
                SelectedSampleRate = newSampleRate;
                SelectedPayloadType = newPayloadType;

                lock (_codecLock) {
                    if (_currentCodecNegotiation != null) {
                        _currentCodecNegotiation.SelectedCodec = newCodec;
                        _currentCodecNegotiation.SelectedPayloadType = newPayloadType;
                        _currentCodecNegotiation.SelectedSampleRate = newSampleRate;
                        _currentCodecNegotiation.IsUsingFallback = (newCodec != AudioCodec.G722);
                    }
                    
                    _lastCodecSwitch = DateTime.UtcNow;
                }

                _logger.LogInformation("✅ Codec switch successful: {PreviousCodec} -> {NewCodec}", previousCodec, newCodec);

                MediaConfigurationChanged?.Invoke(SelectedCodec, SelectedSampleRate, SelectedPayloadType);
                CodecSwitched?.Invoke(previousCodec, newCodec, reason);

                return true;

            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Codec switch failed: {Error}", ex.Message);
                CodecSwitchFailed?.Invoke(newCodec, ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Handle codec health changes
        /// </summary>
        private async void OnCodecHealthChanged(AudioCodec codec, CodecHealthResult healthResult) {
            try {
                _logger.LogInformation("🏥 Codec health changed: {Codec} -> {IsHealthy} ({Issue})", codec, healthResult.IsHealthy, healthResult.Issue ?? "No issues");

                if (!healthResult.IsHealthy && SelectedCodec == codec && _currentCodecNegotiation != null) {
                    _logger.LogWarning("⚠️ Currently selected codec {Codec} became unhealthy, attempting recovery", codec);
                    
                    await RecoverFromCodecFailure(codec, healthResult.Issue ?? "Codec health check failed");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Error handling codec health change: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Recover from codec failure by switching to a healthy alternative
        /// </summary>
        private async Task RecoverFromCodecFailure(AudioCodec failedCodec, string reason) {
            if (_codecFactory == null || _currentCodecNegotiation == null) {
                return;
            }

            try {
                _logger.LogWarning("🚨 Recovering from codec failure: {FailedCodec} - {Reason}", failedCodec, reason);

                var fallbackCodec = _currentCodecNegotiation.FallbackCodec;
                
                if (fallbackCodec != failedCodec && _codecFactory.IsCodecHealthy(fallbackCodec)) {
                    _logger.LogInformation("🔄 Attempting recovery using fallback codec: {FallbackCodec}", fallbackCodec);
                    
                    if (await SwitchCodec(fallbackCodec, $"Recovery from {failedCodec} failure: {reason}")) {
                        _logger.LogInformation("✅ Recovery successful using {FallbackCodec}", fallbackCodec);
                        return;
                    }
                }

                var healthyCodecs = new[] { AudioCodec.PCMA, AudioCodec.PCMU, AudioCodec.G722 }
                    .Where(c => c != failedCodec && _codecFactory.IsCodecHealthy(c))
                    .ToList();

                foreach (var healthyCodec in healthyCodecs) {
                    _logger.LogInformation("🔄 Attempting recovery using healthy codec: {HealthyCodec}", healthyCodec);
                    
                    if (await SwitchCodec(healthyCodec, $"Recovery from {failedCodec} failure: {reason}")) {
                        _logger.LogInformation("✅ Recovery successful using {HealthyCodec}", healthyCodec);
                        return;
                    }
                }

                _logger.LogError("❌ Codec recovery failed: no healthy codecs available");

            } catch (Exception ex) {
                _logger.LogError(ex, "❌ Error during codec recovery: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// Test a codec before switching to ensure it works properly
        /// </summary>
        private async Task<bool> TestCodecBeforeSwitch(AudioCodec codecType) {
            if (_codecFactory == null) {
                return false;
            }

            try {
                return await Task.Run(() => {
                    using var testCodec = _codecFactory.GetCodec(codecType);
                    
                    // Generate test audio data
                    int sampleRate = codecType == AudioCodec.G722 ? 16000 : 8000;
                    int frameSamples = sampleRate * 20 / 1000; // 20ms frame
                    var testData = new byte[frameSamples * 2]; // 16-bit samples
                    
                    // Fill with a simple test pattern
                    for (int i = 0; i < testData.Length; i += 2) {
                        short sample = (short)(Math.Sin(2 * Math.PI * 1000 * i / (sampleRate * 2)) * 8000);
                        testData[i] = (byte)(sample & 0xFF);
                        testData[i + 1] = (byte)((sample >> 8) & 0xFF);
                    }
                    
                    // Test encode
                    var encoded = testCodec.Encode(testData);
                    if (encoded.Length == 0) {
                        _logger.LogWarning("Codec test failed: encode returned empty data");
                        return false;
                    }
                    
                    // Test decode
                    var decoded = testCodec.Decode(encoded);
                    if (decoded.Length == 0) {
                        _logger.LogWarning("Codec test failed: decode returned empty data");
                        return false;
                    }
                    
                    return true;
                });
            } catch (Exception ex) {
                _logger.LogWarning("Codec test failed for {Codec}: {Error}", codecType, ex.Message);
                return false;
            }
        }


        /// <summary>
        /// Get payload type for a codec
        /// </summary>
        private int GetPayloadTypeForCodec(AudioCodec codec) {
            return codec switch {
                AudioCodec.PCMU => 0,
                AudioCodec.PCMA => 8,
                AudioCodec.G722 => 9,
                _ => 8 // Default to PCMA
            };
        }

        /// <summary>
        /// Get sample rate for a codec
        /// </summary>
        private int GetSampleRateForCodec(AudioCodec codec) {
            return codec switch {
                AudioCodec.PCMU => 8000,
                AudioCodec.PCMA => 8000,
                AudioCodec.G722 => 16000,
                _ => 8000 // Default to 8kHz
            };
        }

        /// <summary>
        /// Parse SDP to determine negotiated codec and update configuration
        /// This should follow proper SDP negotiation rules, not hardcoded preferences
        /// </summary>
        private void ParseSdpAndUpdateCodecConfiguration(SDP sdp) {
            try {
                var audioMedia = sdp.Media?.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);
                if (audioMedia?.MediaFormats == null || !audioMedia.MediaFormats.Any()) {
                    _logger.LogWarning("No audio media formats found in SDP");
                    return;
                }

                _logger.LogDebug("SDP contains {Count} audio media formats: [{Formats}]", audioMedia.MediaFormats.Count, string.Join(", ", audioMedia.MediaFormats.Select(f => $"PT:{f.Key}({f.Value.Name()})")));

                KeyValuePair<int, SDPAudioVideoMediaFormat>? selectedFormatPair = null;
                
                foreach (var format in audioMedia.MediaFormats) {
                    var codecName = format.Value.Name();
                    var clockRate = format.Value.ClockRate();
                    
                    _logger.LogDebug("Evaluating SDP format: PT:{PayloadType}, Codec:{CodecName}, ClockRate:{ClockRate}", 
                        format.Key, codecName, clockRate);
                    
                    // Check if we support this codec (handles both static and dynamic PT)
                    if (IsSupportedCodec(format.Key, codecName, clockRate)) {
                        selectedFormatPair = format;
                        
                        _logger.LogInformation("✅ Selected codec via SDP negotiation: PT:{PayloadType}, Codec:{CodecName}, ClockRate:{ClockRate}", format.Key, codecName, clockRate);
                        break;
                    } else {
                        _logger.LogDebug("❌ Skipping unsupported codec: PT:{PayloadType}, Codec:{CodecName}, ClockRate:{ClockRate}", format.Key, codecName, clockRate);
                    }
                }

                if (!selectedFormatPair.HasValue) {
                    _logger.LogWarning("No mutually supported audio codec found in SDP");
                    return;
                }

                var selectedPayloadType = selectedFormatPair.Value.Key;
                var selectedCodecName = selectedFormatPair.Value.Value.Name();
                var selectedClockRate = selectedFormatPair.Value.Value.ClockRate();
                
                // Map to AudioCodec enum based on codec name, not just payload type
                var selectedAudioCodec = MapToAudioCodec(selectedPayloadType, selectedCodecName, selectedClockRate);
                if (!selectedAudioCodec.HasValue) {
                    _logger.LogWarning("Failed to map selected codec to AudioCodec enum: PT:{PayloadType}, Codec:{CodecName}", selectedPayloadType, selectedCodecName);
                    return;
                }

                var previousCodec = SelectedCodec;
                var previousSampleRate = SelectedSampleRate;
                var previousPayloadType = SelectedPayloadType;

                // Update codec configuration based on the actual negotiated codec
                SelectedCodec = selectedAudioCodec.Value;
                SelectedPayloadType = selectedPayloadType;
                
                // Set sample rate based on codec type (internal processing rate)
                switch (selectedAudioCodec.Value) {
                    case AudioCodec.G722:
                        SelectedSampleRate = 16000; // G.722 internal processing is 16kHz
                        break;
                    case AudioCodec.PCMA:
                    case AudioCodec.PCMU:
                        SelectedSampleRate = 8000;  // G.711 is 8kHz
                        break;
                    default:
                        _logger.LogWarning("Unknown codec type: {Codec}", selectedAudioCodec.Value);
                        return;
                }

                if (previousCodec != SelectedCodec || 
                    previousSampleRate != SelectedSampleRate || 
                    previousPayloadType != SelectedPayloadType) {                    
                    _logger.LogInformation("Codec configuration changed via SDP negotiation: {PreviousCodec}@{PreviousSampleRate}Hz (PT:{PreviousPayloadType}) -> {NewCodec}@{NewSampleRate}Hz (PT:{NewPayloadType})", previousCodec, previousSampleRate, previousPayloadType, SelectedCodec, SelectedSampleRate, SelectedPayloadType);

                    MediaConfigurationChanged?.Invoke(SelectedCodec, SelectedSampleRate, SelectedPayloadType);
                } else {
                    _logger.LogDebug("Codec configuration unchanged after SDP parsing: {Codec}@{SampleRate}Hz (PT:{PayloadType})", SelectedCodec, SelectedSampleRate, SelectedPayloadType);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error parsing SDP for codec configuration");
            }
        }

        /// <summary>
        /// Check if we support a given payload type and codec name combination
        /// This handles both static and dynamic payload types
        /// </summary>
        private bool IsSupportedCodec(int payloadType, string codecName, int clockRate) {
            // Normalize codec name for comparison
            var normalizedCodecName = codecName?.ToUpperInvariant().Trim();
            
            // Static payload types (RFC 3551)
            switch (payloadType) {
                case 0: // PCMU
                    return normalizedCodecName == "PCMU" && clockRate == 8000;
                case 8: // PCMA  
                    return normalizedCodecName == "PCMA" && clockRate == 8000;
                case 9: // G722
                    // G.722 standard clock rate is 8000 (even though internal sampling is 16kHz)
                    // But some implementations might incorrectly use 16000
                    if (normalizedCodecName == "G722") {
                        if (clockRate == 8000) {
                            return true; // Standard compliant
                        } else if (clockRate == 16000) {
                            _logger.LogWarning("Non-standard G.722 clock rate {ClockRate} detected (should be 8000), but accepting", clockRate);
                            return true; // Accept but warn
                        }
                    }
                    return false;
            }
            
            // Dynamic payload types (96-127)
            if (payloadType >= 96 && payloadType <= 127) {
                switch (normalizedCodecName) {
                    case "PCMU":
                        return clockRate == 8000;
                    case "PCMA":
                        return clockRate == 8000;
                    case "G722":
                        if (clockRate == 8000) {
                            return true;
                        } else if (clockRate == 16000) {
                            _logger.LogWarning("Non-standard G.722 clock rate {ClockRate} detected for dynamic PT {PayloadType}, but accepting", clockRate, payloadType);
                            return true;
                        }
                        return false;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Map codec name and payload type to AudioCodec enum
        /// </summary>
        private AudioCodec? MapToAudioCodec(int payloadType, string codecName, int clockRate) {
            var normalizedCodecName = codecName?.ToUpperInvariant().Trim();
            
            switch (normalizedCodecName) {
                case "PCMU":
                    return AudioCodec.PCMU;
                case "PCMA":
                    return AudioCodec.PCMA;
                case "G722":
                    return AudioCodec.G722;
                default:
                    return null;
            }
        }
    }
}