using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using SIPSorcery.Media;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;

namespace AI.Caller.Core.Media {
    public interface IAudioBuffer {
        int Count { get; }
        void Write(ReadOnlySpan<short> data);
        bool TryReadFrame(Span<short> destination);
        void Clear();
    }

    public class AudioRingBuffer : IAudioBuffer {
        private readonly short[] _buffer;
        private int _writePos;
        private int _readPos;
        private int _count;
        private readonly object _lock = new();
        private readonly int _capacity;

        public int Count {
            get {
                lock (_lock) {
                    return _count;
                }
            }
        }

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

    /// <summary>
    /// 弹性音频缓冲区 - 支持无限写入（突发缓冲），按需读取（平滑播放）
    /// 用于解决 AIAutoResponder 生成速度远快于播放速度导致的缓冲区溢出问题
    /// </summary>
    public class ElasticAudioBuffer : IAudioBuffer {
        private readonly Queue<short> _buffer = new Queue<short>();
        private readonly object _lock = new();
        private readonly int _maxSamples;

        /// <summary>
        /// 创建带有容量上限的弹性缓冲
        /// </summary>
        /// <param name="maxSamples">默认上限80000采样点(8kHz约10秒)</param>
        public ElasticAudioBuffer(int maxSamples = 80000) {
            _maxSamples = maxSamples;
        }

        public int Count {
            get {
                lock (_lock) {
                    return _buffer.Count;
                }
            }
        }

        public void Write(ReadOnlySpan<short> data) {
            lock (_lock) {
                for (int i = 0; i < data.Length; i++) {
                    _buffer.Enqueue(data[i]);
                }
                
                // 防止无限扩张导致内存泄漏 (Drop oldest)
                while (_buffer.Count > _maxSamples) {
                    _buffer.Dequeue();
                }
            }
        }

        public bool TryReadFrame(Span<short> destination) {
            lock (_lock) {
                if (_buffer.Count < destination.Length) {
                    return false;
                }

                for (int i = 0; i < destination.Length; i++) {
                    destination[i] = _buffer.Dequeue();
                }
                return true;
            }
        }

        public void Clear() {
            lock (_lock) {
                _buffer.Clear();
            }
        }
    }

    public class MonitorMediaSession : IDisposable {
        private readonly ILogger _logger;
        private readonly short[] _mixBuffer;
        private readonly RTCPeerConnection _pc;
        private readonly ElasticAudioBuffer _aiBuffer;
        private readonly MixerAudioSource _audioSource;
        private readonly AudioRingBuffer _customerBuffer;
        private readonly AudioCodecFactory _audioCodecFactory;
        private readonly CancellationTokenSource _mixingCts = new();

        private Task? _mixingTask;
        private IAudioCodec? _codec;
        private int? _currentPayloadType = null;

        private const int SAMPLE_RATE = 8000;
        private const int SAMPLES_PER_FRAME = 160; // 20ms
        private const int BUFFER_SIZE = 8000; // 1秒总容量

        public event Action<byte[]>? OnInterventionAudioReceived;
        public event Action<RTCIceCandidateInit>? OnIceCandidate;
        public event Action<RTCPeerConnectionState>? OnConnectionStateChange;

        public MonitorMediaSession(ILogger logger, AudioCodecFactory codecFactory, WebRTCSettings settings) {
            _logger = logger;
            
            // 🔧 FIX: 使用弹性缓冲区来处理突发的 AI 音频，防止溢出丢包
            _aiBuffer = new ElasticAudioBuffer();
            _mixBuffer = new short[SAMPLES_PER_FRAME];
            _customerBuffer = new AudioRingBuffer(BUFFER_SIZE);
            _audioCodecFactory = codecFactory;

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
        public void AddIceCandidate(RTCIceCandidateInit candidate) {
            _pc.addIceCandidate(candidate);
        }

        public async Task<RTCSessionDescriptionInit> HandleOfferAsync(RTCSessionDescriptionInit offer) {
            _pc.setRemoteDescription(offer);
            var answer = _pc.createAnswer(null);
            await _pc.setLocalDescription(answer);

            try {
                var audioMedia = _pc.localDescription.sdp.Media.FirstOrDefault(m => m.Media == SDPMediaTypesEnum.audio);

                if (audioMedia != null && audioMedia.MediaFormats.Count > 0) {
                    var selectedFormat = audioMedia.MediaFormats.First();
                    _codec = _audioCodecFactory.GetCodec((AudioCodec)audioMedia.MediaFormats.First().Value.ID);
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

        public void SendAudio(byte[] pcmData, bool isAiAudio) {
            IAudioBuffer targetBuffer = isAiAudio ? _aiBuffer : _customerBuffer;
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcmData);
            targetBuffer.Write(samples);
        }

        public void SendAudio(byte[] pcmData, int offset, int length, bool isAiAudio) {
            IAudioBuffer targetBuffer = isAiAudio ? _aiBuffer : _customerBuffer;
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcmData.AsSpan(offset, length));
            targetBuffer.Write(samples);
        }

        /// <summary>
        /// 清空AI音频缓冲区
        /// 用于人工介入时清除已缓存的AI音频
        /// </summary>
        public void ClearAiBuffer() {
            _aiBuffer.Clear();
        }

        public void Dispose() {
            StopMixing();
            _mixingCts.Dispose();

            if (_audioSource != null) {
                _audioSource.OnAudioSourceEncodedSample -= _pc.SendAudio;
                _audioSource.CloseAudio();
            }

            _codec?.Dispose();

            // 清空缓冲区
            _aiBuffer.Clear();
            _customerBuffer.Clear();

            _pc.Close("session closed");
            _pc.Dispose();
        }

        private void StartMixing() {
            if (_mixingTask != null) return;
            _mixingTask = Task.Factory.StartNew(async () => {
                var interval = TimeSpan.FromMilliseconds(20);
                using var timer = new PeriodicTimer(interval);
                try {
                    while (await timer.WaitForNextTickAsync(_mixingCts.Token)) {
                        MixAndSendAudio();
                    }
                } catch (OperationCanceledException) { } catch (Exception ex) {
                    _logger.LogError(ex, "Error in mixing loop");
                }
            }, _mixingCts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
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

            bool hasAi = _aiBuffer.TryReadFrame(aiFrame);
            bool hasCust = _customerBuffer.TryReadFrame(custFrame);

            if (!hasAi && !hasCust) {
                mixSpan.Clear();
                _audioSource.SendAudio(_mixBuffer, SAMPLE_RATE);
                return;
            }

            if (!hasAi || !hasCust) {
                if (hasAi) {
                    aiFrame.CopyTo(mixSpan);
                } else {
                    custFrame.CopyTo(mixSpan);
                }
                _audioSource.SendAudio(_mixBuffer, SAMPLE_RATE);
                return;
            }

            for (int i = 0; i < SAMPLES_PER_FRAME; i++) {
                int mixed = aiFrame[i] + custFrame[i];
                if (mixed > short.MaxValue) mixed = short.MaxValue;
                else if (mixed < short.MinValue) mixed = short.MinValue;
                mixSpan[i] = (short)mixed;
            }

            _audioSource.SendAudio(_mixBuffer, SAMPLE_RATE);
        }

        private void ProcessIncomingRtp(RTPPacket rtp) {
            if (_codec == null) {
                _logger.LogWarning("No codec selected for sending audio.");
                return;
            }

            byte[] pcm = ArrayPool<byte>.Shared.Rent(rtp.Payload.Length * 2);

            try {
                var pcmLength = _codec.Decode(rtp.Payload, pcm);

                if (pcmLength > 0) {
                    if (OnInterventionAudioReceived != null) {
                        byte[] validPcm = new byte[pcmLength];
                        Array.Copy(pcm, validPcm, pcmLength);
                        OnInterventionAudioReceived.Invoke(validPcm);
                    }
                }
            } finally {
                ArrayPool<byte>.Shared.Return(pcm);
            }
        }
    }
}
