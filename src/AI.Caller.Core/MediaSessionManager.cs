using Microsoft.Extensions.Logging;
using SIPSorcery.Net;
using SIPSorcery.SIP.App;
using SIPSorceryMedia.Abstractions;
using System.Net;

namespace AI.Caller.Core {
    public class MediaSessionManager : IDisposable {
        private RTPSession? _mediaSession;
        private RTCPeerConnection? _peerConnection;
        private readonly object _lock = new object();
        private bool _disposed = false;

        private readonly ILogger _logger;

        public event Action<RTCSessionDescriptionInit>? SdpOfferGenerated;
        public event Action<RTCSessionDescriptionInit>? SdpAnswerGenerated;
        public event Action<RTCIceCandidateInit>? IceCandidateGenerated;

        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? AudioDataSent;
        public event Action<IPEndPoint, SDPMediaTypesEnum, RTPPacket>? AudioDataReceived;

        public event Action<RTCPeerConnectionState>? ConnectionStateChanged;

        public MediaSessionManager(ILogger logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task InitializeMediaSession() {
            ThrowIfDisposed();

            RTPSession? tempMediaSession = null;

            lock (_lock) {
                if (_mediaSession != null) {
                    _logger.LogDebug("MediaSession already initialized, skipping.");
                    return;
                }

                try {
                    tempMediaSession = new RTPSession(false, false, false);
                    tempMediaSession.AcceptRtpFromAny = true;

                    var audioTrack = new MediaStreamTrack(
                        SDPMediaTypesEnum.audio,
                        false,
                        [
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMA),
                            new SDPAudioVideoMediaFormat(SDPWellKnownMediaFormatsEnum.PCMU)
                        ]
                    );

                    tempMediaSession.addTrack(audioTrack);
                    tempMediaSession.OnRtpPacketReceived += OnRtpPacketReceived;

                    _mediaSession = tempMediaSession;
                    tempMediaSession = null; // Prevent disposal in catch block

                    _logger.LogInformation("MediaSession initialized successfully.");
                } catch (Exception ex) {
                    _logger.LogError(ex, "Failed to initialize MediaSession");

                    tempMediaSession?.Dispose();
                    _mediaSession = null;
                    throw;
                }
            }

            try {
                await _mediaSession.Start();
                _logger.LogInformation("MediaSession started successfully.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to start MediaSession");

                lock (_lock) {
                    if (_mediaSession != null) {
                        _mediaSession.OnRtpPacketReceived -= OnRtpPacketReceived;
                        _mediaSession.Dispose();
                        _mediaSession = null;
                    }
                }
                throw;
            }
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
                _logger.LogInformation($"Set WebRTC remote description ({description.type}) successfully.");
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to set WebRTC remote description");
                throw;
            }
        }

        public async Task SetSipRemoteDescriptionAsync(RTCSessionDescriptionInit description) {
            ThrowIfDisposed();
            ArgumentNullException.ThrowIfNull(description);

            if (_mediaSession == null) {
                _logger.LogInformation("MediaSession not initialized, initializing now for SIP remote description.");
                await InitializeMediaSession();
            }

            try {
                var sdp = SDP.ParseSDPDescription(description.sdp);
                _mediaSession!.SetRemoteDescription(description.type == RTCSdpType.offer ? SdpType.offer : SdpType.answer, sdp);
                _logger.LogInformation($"Set SIP remote description ({description.type}) successfully on RTPSession.");

                if (sdp.Connection != null && sdp.Media != null) {
                    var audioMedia = sdp.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);
                    if (audioMedia != null && audioMedia.Port > 0) {
                        var remoteEndPoint = new IPEndPoint(
                            IPAddress.Parse(sdp.Connection.ConnectionAddress),
                            audioMedia.Port
                        );
                        _mediaSession.SetDestination(SDPMediaTypesEnum.audio, remoteEndPoint, remoteEndPoint);
                        _logger.LogInformation($"Configured RTPSession destination to {remoteEndPoint} for audio.");
                    } else {
                        _logger.LogWarning("No valid audio media found in SDP, cannot set RTPSession destination.");
                    }
                } else {
                    _logger.LogWarning("Invalid SDP: missing connection or media information.");
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to set SIP remote description");
                throw;
            }
        }

        public void Cancel() {
            if (_mediaSession != null) {
                _mediaSession.OnRtpPacketReceived -= OnRtpPacketReceived;
            }
            if (_peerConnection != null) {
                _peerConnection.OnRtpPacketReceived -= OnForwardMediaToSIP;
            }
        }

        public async Task<RTCSessionDescriptionInit> CreateOfferAsync() {
            ThrowIfDisposed();

            // 确保RTCPeerConnection已初始化
            await EnsurePeerConnectionInitializedAsync();

            lock (_lock) {
                if (_peerConnection == null) {
                    throw new InvalidOperationException("RTCPeerConnection is not initialized");
                }
            }

            try {
                var sdpOffer = _peerConnection.createOffer();
                await _peerConnection.setLocalDescription(sdpOffer);
                _logger.LogInformation("Generated and set SDP Offer for RTCPeerConnection.");

                lock (_lock) {
                    if (_mediaSession != null) {
                        var offerSDP = SDP.ParseSDPDescription(sdpOffer.sdp);
                        _mediaSession.SetRemoteDescription(SdpType.offer, offerSDP);
                        _logger.LogInformation("Set SDP Offer for RTPSession.");
                    }
                }

                SdpOfferGenerated?.Invoke(sdpOffer);
                return sdpOffer;
            } catch (Exception ex) {
                _logger.LogError(ex, "Error generating SDP Offer");
                throw;
            }
        }

        public async Task<RTCSessionDescriptionInit> CreateAnswerAsync() {
            ThrowIfDisposed();

            // 确保RTCPeerConnection已初始化
            await EnsurePeerConnectionInitializedAsync();

            lock (_lock) {
                if (_peerConnection == null) {
                    throw new InvalidOperationException("RTCPeerConnection is not initialized");
                }
            }

            try {
                var answerSdp = _peerConnection.createAnswer(new RTCAnswerOptions());
                await _peerConnection.setLocalDescription(answerSdp);
                _logger.LogInformation("Generated and set SDP Answer for RTCPeerConnection.");

                lock (_lock) {
                    if (_mediaSession != null) {
                        var rtpAnswerSdp = SDP.ParseSDPDescription(answerSdp.sdp);
                        _mediaSession.SetRemoteDescription(SdpType.answer, rtpAnswerSdp);
                        _logger.LogInformation("Set SDP Answer for RTPSession.");
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
                _logger.LogInformation("Browser call initiated successfully with CreateOffer->SetLocalDescription flow");
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
                _logger.LogInformation("Browser incoming call handled successfully with SetRemoteDescription->CreateAnswer->SetLocalDescription flow");
                return answer;
            } catch (Exception ex) {
                _logger.LogError(ex, "Failed to handle browser incoming call");
                throw;
            }
        }

        private void OnRtpPacketReceived(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    _logger.LogDebug($"Skipping non-audio or empty RTP packet: MediaType={mediaType}, PayloadLength={(rtpPacket?.Payload?.Length ?? 0)}");
                    return;
                }

                if (_peerConnection != null && _peerConnection.connectionState == RTCPeerConnectionState.connected) {
                    _peerConnection.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                    _logger.LogTrace($"Forwarded RTP audio to WebRTC: {rtpPacket.Payload.Length} bytes from {remote}");
                    AudioDataReceived?.Invoke(remote, mediaType, rtpPacket);
                } else {
                    _logger.LogWarning($"Cannot forward audio to WebRTC: RTCPeerConnection={(_peerConnection != null ? "Exists" : "Null")}, State={(_peerConnection?.connectionState.ToString() ?? "N/A")}");
                }
            } catch (Exception ex) {
                _logger.LogError($"Error processing RTP audio packet from {remote}: {ex.Message}");
            }
        }

        public void OnForwardMediaToSIP(IPEndPoint remote, SDPMediaTypesEnum mediaType, RTPPacket rtpPacket) {
            try {
                if (mediaType != SDPMediaTypesEnum.audio || rtpPacket?.Payload == null || rtpPacket.Payload.Length == 0) {
                    _logger.LogDebug($"Skipping non-audio or empty RTP packet: MediaType={mediaType}, PayloadLength={(rtpPacket?.Payload?.Length ?? 0)}");
                    return;
                }

                if (_mediaSession != null && !_mediaSession.IsClosed) {
                    var sendToEndPoint = _mediaSession.AudioDestinationEndPoint;
                    if (sendToEndPoint != null) {
                        _mediaSession.SendAudio((uint)rtpPacket.Payload.Length, rtpPacket.Payload);
                        _logger.LogTrace($"Forwarded WebRTC audio to SIP: {rtpPacket.Payload.Length} bytes from {remote} to {sendToEndPoint}");
                        AudioDataSent?.Invoke(remote, mediaType, rtpPacket);
                    } else {
                        _logger.LogError("Cannot forward audio to SIP: dstEndPoint is null.");
                    }
                } else {
                    _logger.LogWarning($"Cannot forward audio to SIP: MediaSession={(_mediaSession != null ? "Exists" : "Null")}, IsClosed={(_mediaSession?.IsClosed ?? true)}");
                }
            } catch (Exception ex) {
                _logger.LogError($"Error forwarding media to SIP: {ex.Message}");
            }
        }

        private void SetupPeerConnectionEvents(RTCPeerConnection? peerConnection = null) {
            var pc = peerConnection ?? _peerConnection;
            if (pc == null) return;

            pc.onconnectionstatechange += (state) => {
                _logger.LogInformation($"RTCPeerConnection state changed to: {state}");
                if (state == RTCPeerConnectionState.connected) {
                    _logger.LogInformation("Peer connection connected.");
                } else if (state == RTCPeerConnectionState.failed || state == RTCPeerConnectionState.disconnected) {
                    _logger.LogWarning($"RTCPeerConnection failed or disconnected: {state}");
                }
                ConnectionStateChanged?.Invoke(state);
            };

            pc.onicecandidate += (candidate) => {
                if (candidate != null) {
                    _logger.LogDebug($"ICE candidate: {candidate.candidate}, Type: {candidate.type}, Address: {candidate.address}, Port: {candidate.port}");
                    if (candidate.candidate != null && candidate.candidate.Contains("typ relay")) {
                        _logger.LogInformation("Using TURN relay for media.");
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

        public RTPSession? MediaSession => _mediaSession;
        public RTCPeerConnection? PeerConnection => _peerConnection;

        private void ThrowIfDisposed() {
            if (_disposed) {
                throw new ObjectDisposedException(nameof(MediaSessionManager));
            }
        }

        private async Task EnsurePeerConnectionInitializedAsync() {
            lock (_lock) {
                if (_peerConnection != null) {
                    return; // 已经初始化
                }
            }

            // 如果MediaSession也没有初始化，先初始化它
            if (_mediaSession == null) {
                await InitializeMediaSession();
            }

            // 使用默认配置初始化RTCPeerConnection
            var defaultConfig = new RTCConfiguration {
                iceServers = new List<RTCIceServer> {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            InitializePeerConnection(defaultConfig);
            _logger.LogInformation("RTCPeerConnection auto-initialized with default configuration");
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

                    if (_mediaSession != null) {
                        try {
                            _mediaSession.OnRtpPacketReceived -= OnRtpPacketReceived;
                            _mediaSession.Dispose();
                        } catch (Exception ex) {
                            _logger.LogError(ex, "Error disposing MediaSession");
                        } finally {
                            _mediaSession = null;
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
    }
}