using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace AI.Caller.Core {
    public sealed class AudioBridge : IAudioBridge {
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<short[]> _outgoingQueue = new();
        private MediaProfile? _profile;
        private bool _isStarted;
        private readonly object _lock = new();

        public event Action<short[]>? IncomingAudioReceived;
        public event Action<short[]>? OutgoingAudioRequested;

        public AudioBridge(ILogger<AudioBridge> logger) {
            _logger = logger;
        }

        public void Initialize(MediaProfile profile) {
            lock (_lock) {
                _profile = profile;
                _logger.LogDebug($"AudioBridge initialized with profile: SampleRate={profile.SampleRate}, SamplesPerFrame={profile.SamplesPerFrame}");
            }
        }

        public void Start() {
            lock (_lock) {
                if (_isStarted) return;
                
                if (_profile == null) {
                    throw new InvalidOperationException("AudioBridge must be initialized before starting");
                }
                
                _isStarted = true;
                _logger.LogInformation("AudioBridge started");
            }
        }

        public void Stop() {
            lock (_lock) {
                if (!_isStarted) return;
                
                _isStarted = false;
                
                // 清空队列
                while (_outgoingQueue.TryDequeue(out _)) { }
                
                _logger.LogInformation("AudioBridge stopped");
            }
        }

        public void ProcessIncomingAudio(byte[] audioData, int sampleRate) {
            if (!_isStarted || _profile == null) return;

            try {
                // 将byte[]转换为short[]
                var samples = ConvertBytesToShorts(audioData);
                
                // 重采样到目标采样率（如果需要）
                if (sampleRate != _profile.SampleRate) {
                    samples = ResampleAudio(samples, sampleRate, _profile.SampleRate);
                }
                
                // 分帧处理
                ProcessAudioFrames(samples, frame => {
                    IncomingAudioReceived?.Invoke(frame);
                });
                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing incoming audio");
            }
        }

        public void InjectOutgoingAudio(short[] audioData) {
            if (!_isStarted || audioData == null || audioData.Length == 0) return;

            try {
                // 分帧并加入队列
                ProcessAudioFrames(audioData, frame => {
                    _outgoingQueue.Enqueue(frame);
                });
                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error injecting outgoing audio");
            }
        }

        public short[] GetNextOutgoingFrame() {
            if (!_isStarted || _profile == null) {
                return Array.Empty<short>();
            }

            // 尝试从队列获取音频帧
            if (_outgoingQueue.TryDequeue(out var frame)) {
                return frame;
            }

            // 如果队列为空，请求新的音频数据
            var requestedFrame = new short[_profile.SamplesPerFrame];
            OutgoingAudioRequested?.Invoke(requestedFrame);
            
            // 检查是否有数据被填充
            bool hasAudio = false;
            for (int i = 0; i < requestedFrame.Length; i++) {
                if (requestedFrame[i] != 0) {
                    hasAudio = true;
                    break;
                }
            }

            return hasAudio ? requestedFrame : new short[_profile.SamplesPerFrame]; // 返回静音帧
        }

        private short[] ConvertBytesToShorts(byte[] audioData) {
            if (audioData.Length % 2 != 0) {
                _logger.LogWarning("Audio data length is not even, truncating last byte");
            }

            int sampleCount = audioData.Length / 2;
            var samples = new short[sampleCount];
            
            for (int i = 0; i < sampleCount; i++) {
                samples[i] = BitConverter.ToInt16(audioData, i * 2);
            }
            
            return samples;
        }

        private short[] ResampleAudio(short[] input, int inputSampleRate, int outputSampleRate) {
            if (inputSampleRate == outputSampleRate) {
                return input;
            }

            // 简单的线性重采样（生产环境建议使用更高质量的重采样算法）
            double ratio = (double)outputSampleRate / inputSampleRate;
            int outputLength = (int)(input.Length * ratio);
            var output = new short[outputLength];

            for (int i = 0; i < outputLength; i++) {
                double sourceIndex = i / ratio;
                int index = (int)sourceIndex;
                
                if (index >= input.Length - 1) {
                    output[i] = input[input.Length - 1];
                } else {
                    // 线性插值
                    double fraction = sourceIndex - index;
                    output[i] = (short)(input[index] * (1 - fraction) + input[index + 1] * fraction);
                }
            }

            return output;
        }

        private void ProcessAudioFrames(short[] audioData, Action<short[]> frameProcessor) {
            if (_profile == null) return;

            int frameSize = _profile.SamplesPerFrame;
            int offset = 0;

            while (offset < audioData.Length) {
                int remainingSamples = audioData.Length - offset;
                int currentFrameSize = Math.Min(frameSize, remainingSamples);
                
                var frame = new short[frameSize];
                Array.Copy(audioData, offset, frame, 0, currentFrameSize);
                
                // 如果帧不完整，剩余部分填充静音
                if (currentFrameSize < frameSize) {
                    for (int i = currentFrameSize; i < frameSize; i++) {
                        frame[i] = 0;
                    }
                }
                
                frameProcessor(frame);
                offset += currentFrameSize;
            }
        }

        public void Dispose() {
            Stop();
            IncomingAudioReceived = null;
            OutgoingAudioRequested = null;
        }
    }
}