using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core.Recording {
    public class AudioBridge : IAudioBridge, IDisposable {
        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        private readonly ConcurrentQueue<AudioBridgeDataEventArgs> _audioBuffer;
        private readonly Timer _healthCheckTimer;

        private IAudioRecordingManager? _recordingManager;
        private bool _isRunning = false;
        private bool _disposed = false;
        private int _sequenceNumber = 0;

        private AudioBridgeStats _stats = new AudioBridgeStats();
        private DateTime _lastHealthCheck = DateTime.UtcNow;

        public event EventHandler<AudioBridgeDataEventArgs>? AudioDataReceived;

        public bool IsRecordingActive => _recordingManager?.IsRecording ?? false;
        public bool IsRunning => _isRunning;

        public AudioBridge(ILogger<AudioBridge> logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _audioBuffer = new ConcurrentQueue<AudioBridgeDataEventArgs>();

            _healthCheckTimer = new Timer(PerformHealthCheck, null, Timeout.Infinite, Timeout.Infinite);

            _logger.LogInformation("AudioBridge initialized");
        }

        public void Start() {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioBridge));

            lock (_lockObject) {
                if (_isRunning)
                    return;

                _isRunning = true;

                _healthCheckTimer.Change(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

                _logger.LogInformation("AudioBridge started");
            }
        }

        public void Stop() {
            lock (_lockObject) {
                if (!_isRunning)
                    return;

                _isRunning = false;

                _healthCheckTimer.Change(Timeout.Infinite, Timeout.Infinite);

                while (_audioBuffer.TryDequeue(out _)) { }

                _logger.LogInformation("AudioBridge stopped");
            }
        }

        public void ForwardAudioData(AudioSource source, byte[] audioData, AudioFormat format) {
            if (_disposed || !_isRunning)
                return;

            if (audioData == null || audioData.Length == 0) {
                _logger.LogTrace("Received empty audio data, skipping");
                return;
            }

            try {
                var eventArgs = new AudioBridgeDataEventArgs(source, audioData, format) {
                    SequenceNumber = Interlocked.Increment(ref _sequenceNumber)
                };

                UpdateStats(source, audioData.Length);

                if (_recordingManager != null && IsRecordingActive) {
                    var audioFrame = new AudioFrame(audioData, format, source) {
                        SequenceNumber = (uint)eventArgs.SequenceNumber,
                        Timestamp = eventArgs.Timestamp
                    };

                    _recordingManager.ProcessAudioFrame(audioFrame);
                    _logger.LogTrace($"Forwarded audio data to recording manager: {audioData.Length} bytes from {source}");
                }

                AudioDataReceived?.Invoke(this, eventArgs);

                _logger.LogTrace($"Audio data forwarded: {audioData.Length} bytes from {source}");
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error forwarding audio data from {source}: {ex.Message}");

                lock (_lockObject) {
                    _stats.IsHealthy = false;
                    _stats.Issues.Add($"Error forwarding audio: {ex.Message}");
                }
            }
        }

        public void RegisterRecordingManager(IAudioRecordingManager recordingManager) {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioBridge));

            lock (_lockObject) {
                if (_recordingManager != null) {
                    _logger.LogWarning("Recording manager already registered, replacing with new instance");
                }

                _recordingManager = recordingManager ?? throw new ArgumentNullException(nameof(recordingManager));
                _logger.LogInformation("Recording manager registered with AudioBridge");
            }
        }

        public void UnregisterRecordingManager() {
            lock (_lockObject) {
                if (_recordingManager != null) {
                    _recordingManager = null;
                    _logger.LogInformation("Recording manager unregistered from AudioBridge");
                }
            }
        }

        public AudioBridgeStats GetStats() {
            lock (_lockObject) {
                return new AudioBridgeStats {
                    TotalFramesForwarded = _stats.TotalFramesForwarded,
                    TotalBytesForwarded = _stats.TotalBytesForwarded,
                    FramesBySource = new Dictionary<AudioSource, long>(_stats.FramesBySource),
                    BytesBySource = new Dictionary<AudioSource, long>(_stats.BytesBySource),
                    LastDataReceived = _stats.LastDataReceived,
                    IsHealthy = _stats.IsHealthy,
                    Issues = new List<string>(_stats.Issues)
                };
            }
        }

        public void ResetStats() {
            lock (_lockObject) {
                _stats = new AudioBridgeStats {
                    IsHealthy = true
                };
                _logger.LogInformation("AudioBridge statistics reset");
            }
        }

        private void UpdateStats(AudioSource source, int dataLength) {
            lock (_lockObject) {
                _stats.TotalFramesForwarded++;
                _stats.TotalBytesForwarded += dataLength;
                _stats.LastDataReceived = DateTime.UtcNow;

                if (!_stats.FramesBySource.ContainsKey(source)) {
                    _stats.FramesBySource[source] = 0;
                    _stats.BytesBySource[source] = 0;
                }

                _stats.FramesBySource[source]++;
                _stats.BytesBySource[source] += dataLength;

                if (_stats.Issues.Count > 0) {
                    _stats.Issues.Clear();
                    _stats.IsHealthy = true;
                }
            }
        }

        private void PerformHealthCheck(object? state) {
            if (_disposed || !_isRunning)
                return;

            try {
                lock (_lockObject) {
                    var now = DateTime.UtcNow;
                    var timeSinceLastData = now - _stats.LastDataReceived;

                    if (timeSinceLastData > TimeSpan.FromSeconds(10) && _stats.TotalFramesForwarded > 0) {
                        _stats.IsHealthy = false;
                        var issue = $"No audio data received for {timeSinceLastData.TotalSeconds:F1} seconds";
                        if (!_stats.Issues.Contains(issue)) {
                            _stats.Issues.Add(issue);
                            _logger.LogWarning($"AudioBridge health check: {issue}");
                        }
                    }

                    var bufferSize = _audioBuffer.Count;
                    if (bufferSize > 100) {
                        _stats.IsHealthy = false;
                        var issue = $"Audio buffer overflow: {bufferSize} items";
                        if (!_stats.Issues.Contains(issue)) {
                            _stats.Issues.Add(issue);
                            _logger.LogWarning($"AudioBridge health check: {issue}");
                        }

                        var itemsToRemove = bufferSize / 2;
                        for (int i = 0; i < itemsToRemove && _audioBuffer.TryDequeue(out _); i++) { }

                        _logger.LogInformation($"Cleared {itemsToRemove} items from audio buffer");
                    }

                    _lastHealthCheck = now;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error during AudioBridge health check: {ex.Message}");
            }
        }

        public void Dispose() {
            if (_disposed)
                return;

            _disposed = true;

            try {
                Stop();
                _healthCheckTimer?.Dispose();

                lock (_lockObject) {
                    _recordingManager = null;
                }

                _logger.LogInformation("AudioBridge disposed");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error disposing AudioBridge");
            }
        }
    }
}