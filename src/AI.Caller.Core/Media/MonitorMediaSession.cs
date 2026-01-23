using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;

namespace AI.Caller.Core.Media {
    public class MonitorMediaSession : IDisposable {
        private readonly ILogger _logger;
        private readonly RTCPeerConnection _pc;
        private readonly MixerAudioSource _audioSource;

        private readonly IAudioCodec _codecPCMA;
        private readonly IAudioCodec _codecPCMU;

        private readonly CancellationTokenSource _mixingCts = new();
        private readonly ConcurrentQueue<short> _aiBuffer = new();
        private readonly ConcurrentQueue<short> _customerBuffer = new();
        
        private Task? _mixingTask;
        private bool _isAiBuffering = true;
        private bool _isCustomerBuffering = true;

        private const int SAMPLE_RATE = 8000;
        private const int SAMPLES_PER_FRAME = 160; // 20ms @ 8kHz
        private const int MAX_BUFFER_SIZE = 2400;  // 300ms，超过这个值说明积压了，需要丢弃旧数据
        private const int START_BUFFER_THRESHOLD = 480; // 只有当缓冲区积攒了 3 帧 (60ms) 数据后才开始混合，抵抗网络抖动

        public event Action<byte[]>? OnInterventionAudioReceived;
        public event Action<RTCIceCandidateInit>? OnIceCandidate;
        public event Action<RTCPeerConnectionState>? OnConnectionStateChange;

        public MonitorMediaSession(ILogger logger, AudioCodecFactory codecFactory, WebRTCSettings settings) {
            _logger = logger;

            _codecPCMA = codecFactory.GetCodec(AudioCodec.PCMA);
            _codecPCMU = codecFactory.GetCodec(AudioCodec.PCMU);

            var config = new RTCConfiguration {
                iceServers = settings.GetRTCIceServers()
            };

            _pc = new RTCPeerConnection(config);

            _audioSource = new MixerAudioSource(new AudioEncoder());
            _audioSource.RestrictFormats(f =>f.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMA || f.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMU);

            var audioTrack = new MediaStreamTrack(_audioSource.GetAudioSourceFormats(), MediaStreamStatusEnum.SendRecv);
            _pc.addTrack(audioTrack);

            _audioSource.OnAudioSourceEncodedSample += _pc.SendAudio;           

            _pc.onicecandidate += (candidate) => {
                if (candidate != null) {
                    OnIceCandidate?.Invoke(new RTCIceCandidateInit {
                        sdpMid = candidate.sdpMid,
                        candidate = candidate.candidate,
                        sdpMLineIndex = candidate.sdpMLineIndex
                    });
                }
            };

            _pc.onconnectionstatechange += (state) => {
                _logger.LogInformation($"Monitor PC State: {state}");
                OnConnectionStateChange?.Invoke(state);

                if (state == RTCPeerConnectionState.connected) {
                    StartMixing();
                } else if (state == RTCPeerConnectionState.closed || state == RTCPeerConnectionState.failed) {
                    StopMixing();
                }
            };

            _pc.OnRtpPacketReceived += (IPEndPoint remote, SDPMediaTypesEnum media, RTPPacket rtp) => {
                if (media == SDPMediaTypesEnum.audio) {
                    ProcessIncomingRtp(rtp);
                }
            };
        }

        private void StartMixing() {
            if (_mixingTask != null) return;

            _mixingTask = Task.Run(async () => {
                var interval = TimeSpan.FromMilliseconds(20);
                using var timer = new PeriodicTimer(interval);

                try {
                    while (await timer.WaitForNextTickAsync(_mixingCts.Token)) {
                        MixAndSendAudio();
                    }
                } catch (OperationCanceledException) { } catch (Exception ex) {
                    _logger.LogError(ex, "Error in mixing loop");
                }
            });
        }

        private void StopMixing() {
            if (!_mixingCts.IsCancellationRequested) {
                _mixingCts.Cancel();
            }
        }

        private void MixAndSendAudio() {
            if (_pc.connectionState != RTCPeerConnectionState.connected) return;

            short[] mixBuffer = new short[SAMPLES_PER_FRAME];

            if (_isCustomerBuffering) {
                if (_customerBuffer.Count >= START_BUFFER_THRESHOLD) {
                    _isCustomerBuffering = false;
                }
            }
            
            if (_isAiBuffering) {
                if (_aiBuffer.Count >= START_BUFFER_THRESHOLD) {
                    _isAiBuffering = false;
                }
            }
            bool canPlayCust = !_isCustomerBuffering;
            bool canPlayAi = !_isAiBuffering;

            if (!canPlayCust && !canPlayAi) {
                _audioSource.SendAudio(mixBuffer, SAMPLE_RATE);
                return;
            }

            for (int i = 0; i < SAMPLES_PER_FRAME; i++) {
                int sampleCust = 0;
                int sampleAi = 0;

                if (canPlayCust) {
                    _customerBuffer.TryDequeue(out short s);
                    sampleCust = s;
                }

                if (canPlayAi) {
                    _aiBuffer.TryDequeue(out short s);
                    sampleAi = s; 
                }

                int mixed = sampleCust + sampleAi;

                if (mixed > short.MaxValue) mixed = short.MaxValue;
                else if (mixed < short.MinValue) mixed = short.MinValue;

                mixBuffer[i] = (short)mixed;
            }

            _audioSource.SendAudio(mixBuffer, SAMPLE_RATE);
        }

        public void SendAudio(byte[] pcmData, bool isAiAudio) {
            var targetBuffer = isAiAudio ? _aiBuffer : _customerBuffer;

            for (int i = 0; i < pcmData.Length; i += 2) {
                if (i + 1 < pcmData.Length) {
                    short sample = (short)(pcmData[i] | (pcmData[i + 1] << 8));
                    targetBuffer.Enqueue(sample);
                }
            }

            int dropCount = 0;
            while (targetBuffer.Count > MAX_BUFFER_SIZE) {
                targetBuffer.TryDequeue(out _);
                dropCount++;
            }

            if (dropCount > 0) {
                if (dropCount > 500) {
                    _logger.LogWarning($"Dropped {dropCount} samples from {(isAiAudio ? "AI" : "Customer")} buffer to sync latency.");
                }
            }
        }

        private void ProcessIncomingRtp(RTPPacket rtp) {
            byte[]? pcm = null;

            if (rtp.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMA) {
                pcm = _codecPCMA.Decode(rtp.Payload);
            } else if (rtp.Header.PayloadType == (int)SDPWellKnownMediaFormatsEnum.PCMU) {
                pcm = _codecPCMU.Decode(rtp.Payload);
            }

            if (pcm != null && pcm.Length > 0) {
                OnInterventionAudioReceived?.Invoke(pcm);
            }
        }

        public async Task<RTCSessionDescriptionInit> HandleOfferAsync(RTCSessionDescriptionInit offer) {
            _pc.setRemoteDescription(offer);
            var answer = _pc.createAnswer(null);
            await _pc.setLocalDescription(answer);

            try {
                var audioMedia = _pc.localDescription.sdp.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);

                if (audioMedia != null && audioMedia.MediaFormats.Count > 0) {                    
                    var selectedFormat = audioMedia.MediaFormats.First();
                    _audioSource.SetAudioSourceFormat(selectedFormat.Value.ToAudioFormat());
                } else {
                    _logger.LogWarning("Monitor WebRTC: No audio media found in local SDP answer.");
                }
                _audioSource?.StartAudio();                
            }catch( Exception ex) {
                _logger.LogError(ex, "Error determining negotiated audio format.");
            }

            return answer;
        }

        public void AddIceCandidate(RTCIceCandidateInit candidate) {
            _pc.addIceCandidate(candidate);
        }

        public void Dispose() {
            StopMixing();
            _mixingCts.Dispose();

            if (_audioSource != null) {
                _audioSource.OnAudioSourceEncodedSample -= _pc.SendAudio;
                _audioSource.CloseAudio();
            }

            _pc.Close("session closed");
            _pc.Dispose();
        }
    }
}
