using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Collections.Concurrent;
using System.Net;
using System.Runtime.InteropServices;

namespace AI.Caller.Core.Media {
    public class AudioRingBuffer {
        private readonly short[] _buffer;
        private int _writePos;
        private int _readPos;
        private int _count;
        private readonly object _lock = new();
        private readonly int _capacity;

        private const int MAX_LATENCY_SAMPLES = 1600;

        private const int CATCH_UP_DROP_SAMPLES = 160;

        public AudioRingBuffer(int size) {
            _capacity = size;
            _buffer = new short[size];
        }

        public void Write(ReadOnlySpan<short> data) {
            lock (_lock) {
                int futureCount = _count + data.Length;
                if (futureCount > MAX_LATENCY_SAMPLES) {
                    int samplesToDrop = futureCount - MAX_LATENCY_SAMPLES;
                    samplesToDrop = Math.Max(samplesToDrop, CATCH_UP_DROP_SAMPLES);

                    _readPos = (_readPos + samplesToDrop) % _capacity;
                    _count -= samplesToDrop;

                    if (_count < 0) _count = 0;
                }

                for (int i = 0; i < data.Length; i++) {
                    _buffer[_writePos] = data[i];
                    _writePos = (_writePos + 1) % _capacity;
                }
                _count += data.Length;

                if (_count > _capacity) {
                    _count = _capacity;
                    _readPos = _writePos;
                }
            }
        }

        public bool TryReadFrame(Span<short> destination) {
            lock (_lock) {
                if (_count < destination.Length) {
                    return false;
                }

                for (int i = 0; i < destination.Length; i++) {
                    destination[i] = _buffer[_readPos];
                    _readPos = (_readPos + 1) % _capacity;
                }
                _count -= destination.Length;
                return true;
            }
        }

        public void Clear() {
            lock (_lock) {
                _count = 0; _readPos = 0; _writePos = 0;
            }
        }
    }

    public class MonitorMediaSession : IDisposable {
        private readonly ILogger _logger;
        private readonly RTCPeerConnection _pc;
        private readonly MixerAudioSource _audioSource;

        private readonly IAudioCodec _codecPCMA;
        private readonly IAudioCodec _codecPCMU;

        private readonly CancellationTokenSource _mixingCts = new();
        private readonly AudioRingBuffer _aiBuffer;
        private readonly AudioRingBuffer _customerBuffer;
        private readonly AudioRingBuffer _interventionBuffer;

        private Task? _mixingTask;

        private readonly short[] _mixBuffer;
        private const int SAMPLE_RATE = 8000;
        private const int SAMPLES_PER_FRAME = 160; // 20ms
        private const int BUFFER_SIZE = 8000; // 1秒总容量

        public event Action<byte[]>? OnInterventionAudioReceived;
        public event Action<RTCIceCandidateInit>? OnIceCandidate;
        public event Action<RTCPeerConnectionState>? OnConnectionStateChange;

        public MonitorMediaSession(ILogger logger, AudioCodecFactory codecFactory, WebRTCSettings settings) {
            _logger = logger;
            _codecPCMA = codecFactory.GetCodec(AudioCodec.PCMA);
            _codecPCMU = codecFactory.GetCodec(AudioCodec.PCMU);

            _aiBuffer = new AudioRingBuffer(BUFFER_SIZE);
            _customerBuffer = new AudioRingBuffer(BUFFER_SIZE);
            _interventionBuffer = new AudioRingBuffer(BUFFER_SIZE);
            _mixBuffer = new short[SAMPLES_PER_FRAME];

            var config = new RTCConfiguration {
                iceServers = settings.GetRTCIceServers()
            };

            _pc = new RTCPeerConnection(config);

            _audioSource = new MixerAudioSource(new AudioEncoder());
            _audioSource.RestrictFormats(f => f.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMA || f.FormatID == (int)SDPWellKnownMediaFormatsEnum.PCMU);

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

            Span<short> mixSpan = _mixBuffer.AsSpan();

            Span<short> aiFrame = stackalloc short[SAMPLES_PER_FRAME];
            Span<short> custFrame = stackalloc short[SAMPLES_PER_FRAME];
            Span<short> intFrame = stackalloc short[SAMPLES_PER_FRAME];

            bool hasAi = _aiBuffer.TryReadFrame(aiFrame);
            bool hasCust = _customerBuffer.TryReadFrame(custFrame);
            bool hasInt = _interventionBuffer.TryReadFrame(intFrame);

            if (!hasAi && !hasCust && !hasInt) {
                mixSpan.Clear();
                _audioSource.SendAudio(_mixBuffer, SAMPLE_RATE);
                return;
            }

            for (int i = 0; i < SAMPLES_PER_FRAME; i++) {
                int sample = 0;
                if (hasAi) sample += aiFrame[i];
                if (hasCust) sample += custFrame[i];
                if (hasInt) sample += intFrame[i];

                if (sample > short.MaxValue) sample = short.MaxValue;
                else if (sample < short.MinValue) sample = short.MinValue;

                mixSpan[i] = (short)sample;
            }

            _audioSource.SendAudio(_mixBuffer, SAMPLE_RATE);
        }

        public void SendAudio(byte[] pcmData, bool isAiAudio) {
            var targetBuffer = isAiAudio ? _aiBuffer : _customerBuffer;
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcmData);
            targetBuffer.Write(samples);
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
                ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcm);
                _interventionBuffer.Write(samples);
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
            } catch (Exception ex) {
                _logger.LogError(ex, "Error setting audio format from SDP");
            }

            _audioSource?.StartAudio();
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
