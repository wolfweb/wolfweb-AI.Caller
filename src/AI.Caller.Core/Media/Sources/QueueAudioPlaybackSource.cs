using System.Collections.Concurrent;
using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Media.Sources {
    public sealed class QueueAudioPlaybackSource : IAudioPlaybackSource, IPlaybackMeter, IPausablePlayback {

        private enum PlaybackState { Stopped, Buffering, Playing }

        private readonly ConcurrentQueue<byte[]> _queue = new();
        private readonly object _bufferLock = new();
        private readonly ILogger _logger;

        private volatile bool _paused;

        private PlaybackState _state = PlaybackState.Stopped;
        private byte[] _buffer = Array.Empty<byte>();
        private int _samplesPerFrame = 160;
        private int _prebufferThresholdBytes;
        private int _totalBufferedBytes;
        private float _playbackRms;

        public bool IsPaused => _paused;
        public float PlaybackRms => _playbackRms;

        public QueueAudioPlaybackSource(ILogger logger) {
            _logger = logger;
        }

        public void Init(MediaProfile profile) {
            const int prebufferDurationMs = 200; // Default prebuffer time
            _samplesPerFrame = profile.SamplesPerFrame;
            _prebufferThresholdBytes = profile.SampleRate * 2 * prebufferDurationMs / 1000;
            _logger.LogDebug($"QueueAudioPlaybackSource initialized. SamplesPerFrame: {_samplesPerFrame}, PrebufferThreshold: {_prebufferThresholdBytes} bytes.");
        }

        public Task StartAsync(CancellationToken ct) {
            lock (_bufferLock) {
                _state = PlaybackState.Buffering;
                _paused = false;
                _playbackRms = 0f;
                _buffer = Array.Empty<byte>();
                _totalBufferedBytes = 0;
                while (_queue.TryDequeue(out _)) { }
            }
            _logger.LogDebug("QueueAudioPlaybackSource started. State: Buffering.");
            return Task.CompletedTask;
        }

        public byte[] ReadNextPcmFrame() {
            int frameBytes = _samplesPerFrame * 2;
            if (_state == PlaybackState.Stopped || _paused) {
                return new byte[frameBytes];
            }

            if (_state == PlaybackState.Buffering) {
                lock (_bufferLock) {
                    if (_state == PlaybackState.Buffering) {
                        _logger.LogDebug("Buffering... returning silence.");
                        return new byte[frameBytes];
                    }
                }
            }

            byte[] outputFrame;
            int bytesConsumed = 0;
            lock (_bufferLock) {
                var workingBuffer = new List<byte>(_buffer);
                while (workingBuffer.Count < frameBytes && _queue.TryDequeue(out var frame)) {
                    workingBuffer.AddRange(frame);
                }

                if (workingBuffer.Count < frameBytes) {
                    _buffer = workingBuffer.ToArray();
                    if (workingBuffer.Count > 0) {
                        _logger.LogDebug($"Partial frame data available ({workingBuffer.Count} bytes), but not enough for a full frame ({frameBytes} bytes). Returning silence and re-buffering.");
                    } else {
                        _logger.LogDebug("Buffer empty, switching back to Buffering state.");
                    }
                    _state = PlaybackState.Buffering;
                    outputFrame = new byte[frameBytes];
                } else {
                    outputFrame = workingBuffer.Take(frameBytes).ToArray();
                    _buffer = workingBuffer.Skip(frameBytes).ToArray();
                    bytesConsumed = frameBytes;
                }
                _totalBufferedBytes -= bytesConsumed;
            }

            UpdatePlaybackRms(outputFrame);
            return outputFrame;
        }

        public Task StopAsync() {
            lock (_bufferLock) {
                _state = PlaybackState.Stopped;
                _paused = false;
                _buffer = Array.Empty<byte>();
                _totalBufferedBytes = 0;
                while (_queue.TryDequeue(out _)) { }
            }
            _logger.LogDebug("QueueAudioPlaybackSource stopped.");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() {
            StopAsync();
            _logger.LogDebug("QueueAudioPlaybackSource disposed.");
            return ValueTask.CompletedTask;
        }

        public void Enqueue(ReadOnlySpan<byte> pcm) {
            if (_state == PlaybackState.Stopped) return;

            var copy = new byte[pcm.Length];
            pcm.CopyTo(copy);
            _queue.Enqueue(copy);

            lock (_bufferLock) {
                _totalBufferedBytes += pcm.Length;
                if (_state == PlaybackState.Buffering && _totalBufferedBytes >= _prebufferThresholdBytes) {
                    _state = PlaybackState.Playing;
                    _logger.LogDebug($"Pre-buffering complete ({_totalBufferedBytes} bytes). State: Playing.");
                }
            }
        }

        public void Pause() {
            lock (_bufferLock) { _paused = true; }
        }

        public void Resume() {
            lock (_bufferLock) { _paused = false; }
        }

        private void UpdatePlaybackRms(byte[] frame) {
            if (frame == null || frame.Length < 2) { _playbackRms = 0f; return; }

            if (frame.Length % 2 != 0) { _playbackRms = 0f; return; }

            double sumSq = 0;
            int sampleCount = frame.Length / 2;

            for (int i = 0; i < sampleCount; i++) {
                short sample = (short)(frame[i * 2] | (frame[i * 2 + 1] << 8));
                float v = sample / 32768f;
                sumSq += v * v;
            }
            _playbackRms = (float)Math.Sqrt(sumSq / sampleCount);
        }
    }
}