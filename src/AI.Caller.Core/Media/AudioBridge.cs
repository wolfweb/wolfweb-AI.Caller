using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Models;
using AI.Caller.Core.Media;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace AI.Caller.Core {
    public sealed class AudioBridge : IAudioBridge {
        private readonly ILogger _logger;
        private readonly ConcurrentQueue<byte[]> _outgoingQueue = new();
        private MediaProfile? _profile;
        private bool _isStarted;
        private readonly object _lock = new();

        public event Action<byte[]>? IncomingAudioReceived;
        public event Action<byte[]>? OutgoingAudioRequested;

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
                
                while (_outgoingQueue.TryDequeue(out _)) { }
                
                _logger.LogInformation("AudioBridge stopped");
            }
        }

        public void ProcessIncomingAudio(byte[] audioData, int sampleRate) {
            if (!_isStarted || _profile == null) return;

            try {
                byte[] processedData = audioData;
                if (sampleRate != _profile.SampleRate) {
                    _logger.LogWarning($"Sample rate mismatch: input={sampleRate}, expected={_profile.SampleRate}. Using AudioResampler.");
                    using var resampler = new AudioResampler<byte>(sampleRate, _profile.SampleRate, _logger);
                    processedData = resampler.Resample(audioData);
                }
                
                ProcessAudioFrames(processedData, frame => {
                    IncomingAudioReceived?.Invoke(frame);
                });
                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error processing incoming audio");
            }
        }

        public void InjectOutgoingAudio(byte[] audioData) {
            if (!_isStarted || audioData == null || audioData.Length == 0) return;

            try {
                ProcessAudioFrames(audioData, frame => {
                    _outgoingQueue.Enqueue(frame);
                });
                
            } catch (Exception ex) {
                _logger.LogError(ex, "Error injecting outgoing audio");
            }
        }

        public byte[] GetNextOutgoingFrame() {
            if (!_isStarted || _profile == null) {
                return Array.Empty<byte>();
            }

            if (_outgoingQueue.TryDequeue(out var frame)) {
                return frame;
            }

            var frameBytes = _profile.SamplesPerFrame * 2;
            var requestedFrame = new byte[frameBytes];
            OutgoingAudioRequested?.Invoke(requestedFrame);
            
            bool hasAudio = false;
            for (int i = 0; i < requestedFrame.Length; i++) {
                if (requestedFrame[i] != 0) {
                    hasAudio = true;
                    break;
                }
            }

            return hasAudio ? requestedFrame : new byte[frameBytes];
        }


        private void ProcessAudioFrames(byte[] audioData, Action<byte[]> frameProcessor) {
            if (_profile == null) return;

            int frameBytes = _profile.SamplesPerFrame * 2; 
            int offset = 0;

            while (offset < audioData.Length) {
                int remainingBytes = audioData.Length - offset;
                int currentFrameBytes = Math.Min(frameBytes, remainingBytes);
                
                var frame = new byte[frameBytes];
                Array.Copy(audioData, offset, frame, 0, currentFrameBytes);
                
                if (currentFrameBytes < frameBytes) {
                    for (int i = currentFrameBytes; i < frameBytes; i++) {
                        frame[i] = 0;
                    }
                }
                
                frameProcessor(frame);
                offset += currentFrameBytes;
            }
        }

        public void Dispose() {
            Stop();
            IncomingAudioReceived = null;
            OutgoingAudioRequested = null;
        }
    }
}