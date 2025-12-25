using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;

namespace AI.Caller.Core.Media {
    /// <summary>
    /// Codec health monitoring result
    /// </summary>
    public class CodecHealthResult {
        public bool IsHealthy { get; set; }
        public string? Issue { get; set; }
        public DateTime LastChecked { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public int TestFramesProcessed { get; set; }
        public double SuccessRate { get; set; }
    }

    /// <summary>
    /// Monitors codec health and detects issues like "snow noise" or encoding failures
    /// </summary>
    public class CodecHealthMonitor : IDisposable {
        private readonly ILogger<CodecHealthMonitor> _logger;
        private readonly AudioCodecFactory _codecFactory;
        private readonly ConcurrentDictionary<AudioCodec, CodecHealthResult> _healthResults = new();
        private readonly Timer? _healthCheckTimer;
        private readonly object _monitorLock = new object();
        private bool _disposed = false;

        // Health check configuration
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(2);
        private readonly int _testFrameCount = 5;
        private readonly double _minimumSuccessRate = 0.8; // 80% success rate required

        public event Action<AudioCodec, CodecHealthResult>? CodecHealthChanged;

        public CodecHealthMonitor(ILogger<CodecHealthMonitor> logger, AudioCodecFactory codecFactory) {
            _logger = logger;
            _codecFactory = codecFactory;
            
            // Initialize health results
            InitializeHealthResults();
            
            // Start periodic health checks
            _healthCheckTimer = new Timer(PerformPeriodicHealthCheck, null, TimeSpan.FromSeconds(10), _healthCheckInterval);
            
            _logger.LogInformation("CodecHealthMonitor initialized with {Interval} check interval", _healthCheckInterval);
        }

        /// <summary>
        /// Check if a specific codec is healthy
        /// </summary>
        public bool IsCodecHealthy(AudioCodec codecType) {
            if (_healthResults.TryGetValue(codecType, out var result)) {
                return result.IsHealthy;
            }
            
            // If not tested yet, perform immediate check
            return PerformImmediateHealthCheck(codecType).IsHealthy;
        }

        /// <summary>
        /// Get detailed health information for a codec
        /// </summary>
        public CodecHealthResult GetCodecHealth(AudioCodec codecType) {
            if (_healthResults.TryGetValue(codecType, out var result)) {
                return result;
            }
            
            // Perform immediate check if not available
            return PerformImmediateHealthCheck(codecType);
        }

        /// <summary>
        /// Get health status for all codecs
        /// </summary>
        public IReadOnlyDictionary<AudioCodec, CodecHealthResult> GetAllCodecHealth() {
            return _healthResults.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        /// <summary>
        /// Force a health check for a specific codec
        /// </summary>
        public CodecHealthResult ForceHealthCheck(AudioCodec codecType) {
            return PerformImmediateHealthCheck(codecType);
        }

        /// <summary>
        /// Force health check for all codecs
        /// </summary>
        public void ForceHealthCheckAll() {
            _logger.LogInformation("🔍 Forcing health check for all codecs");
            
            var codecs = new[] { AudioCodec.PCMA, AudioCodec.PCMU, AudioCodec.G722 };
            
            Parallel.ForEach(codecs, codec => {
                try {
                    PerformImmediateHealthCheck(codec);
                } catch (Exception ex) {
                    _logger.LogError(ex, "Error during forced health check for {Codec}", codec);
                }
            });
        }

        private void InitializeHealthResults() {
            var codecs = new[] { AudioCodec.PCMA, AudioCodec.PCMU, AudioCodec.G722 };
            
            foreach (var codec in codecs) {
                _healthResults[codec] = new CodecHealthResult {
                    IsHealthy = true, // Assume healthy initially
                    LastChecked = DateTime.UtcNow,
                    SuccessRate = 1.0
                };
            }
        }

        private void PerformPeriodicHealthCheck(object? state) {
            if (_disposed) return;

            try {
                _logger.LogDebug("🔍 Performing periodic codec health check");
                
                var codecs = new[] { AudioCodec.PCMA, AudioCodec.PCMU, AudioCodec.G722 };
                
                foreach (var codec in codecs) {
                    try {
                        PerformImmediateHealthCheck(codec);
                    } catch (Exception ex) {
                        _logger.LogError(ex, "Error during periodic health check for {Codec}", codec);
                        
                        // Mark as unhealthy on exception
                        var result = new CodecHealthResult {
                            IsHealthy = false,
                            Issue = $"Health check exception: {ex.Message}",
                            LastChecked = DateTime.UtcNow,
                            SuccessRate = 0.0
                        };
                        
                        UpdateHealthResult(codec, result);
                    }
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "Error during periodic health check");
            }
        }

        private CodecHealthResult PerformImmediateHealthCheck(AudioCodec codecType) {
            lock (_monitorLock) {
                _logger.LogDebug("🔍 Performing immediate health check for {Codec}", codecType);
                
                var startTime = DateTime.UtcNow;
                var result = new CodecHealthResult {
                    LastChecked = startTime,
                    TestFramesProcessed = 0,
                    IsHealthy = true
                };

                try {
                    // Create codec instance for testing
                    using var codec = _codecFactory.GetCodec(codecType);
                    
                    int successfulFrames = 0;
                    var testResults = new List<bool>();

                    // Test multiple frames to get reliable results
                    for (int i = 0; i < _testFrameCount; i++) {
                        try {
                            bool frameTestResult = TestCodecFrame(codec, codecType);
                            testResults.Add(frameTestResult);
                            
                            if (frameTestResult) {
                                successfulFrames++;
                            }
                            
                            result.TestFramesProcessed++;
                        } catch (Exception ex) {
                            _logger.LogWarning("Frame test {FrameIndex} failed for {Codec}: {Error}", i, codecType, ex.Message);
                            testResults.Add(false);
                        }
                    }

                    // Calculate success rate
                    result.SuccessRate = testResults.Count > 0 ? (double)successfulFrames / testResults.Count : 0.0;
                    result.ResponseTime = DateTime.UtcNow - startTime;

                    // Determine health status
                    result.IsHealthy = result.SuccessRate >= _minimumSuccessRate;
                    
                    if (!result.IsHealthy) {
                        result.Issue = $"Low success rate: {result.SuccessRate:P1} (minimum: {_minimumSuccessRate:P1})";
                        _logger.LogWarning("❌ Codec {Codec} health check failed: {Issue}", codecType, result.Issue);
                    } else {
                        _logger.LogDebug("✅ Codec {Codec} health check passed: {SuccessRate:P1} success rate", 
                            codecType, result.SuccessRate);
                    }

                } catch (Exception ex) {
                    result.IsHealthy = false;
                    result.Issue = $"Health check exception: {ex.Message}";
                    result.ResponseTime = DateTime.UtcNow - startTime;
                    
                    _logger.LogError(ex, "❌ Codec {Codec} health check failed with exception", codecType);
                }

                // Update health result and notify if changed
                UpdateHealthResult(codecType, result);
                
                return result;
            }
        }

        private bool TestCodecFrame(IAudioCodec codec, AudioCodec codecType) {
            try {
                // Generate test audio data based on codec sample rate
                int sampleRate = codecType == AudioCodec.G722 ? 16000 : 8000;
                int frameSamples = sampleRate * 20 / 1000; // 20ms frame
                byte[] testPcm = GenerateTestAudio(frameSamples);

                // Test encode
                var encoded = codec.Encode(testPcm);
                if (encoded.Length == 0) {
                    _logger.LogWarning("Encode test failed for {Codec}: empty output", codecType);
                    return false;
                }

                // Test decode
                var decoded = codec.Decode(encoded);
                if (decoded.Length == 0) {
                    _logger.LogWarning("Decode test failed for {Codec}: empty output", codecType);
                    return false;
                }

                // Check for "snow noise" or other audio artifacts
                if (DetectAudioArtifacts(decoded, testPcm)) {
                    _logger.LogWarning("Audio artifacts detected in {Codec} output", codecType);
                    return false;
                }

                return true;
            } catch (Exception ex) {
                _logger.LogWarning("Codec frame test failed for {Codec}: {Error}", codecType, ex.Message);
                return false;
            }
        }

        private static byte[] GenerateTestAudio(int samples) {
            // Generate a simple sine wave test signal
            var pcmData = new byte[samples * 2]; // 16-bit samples
            const double frequency = 1000.0; // 1kHz test tone
            const int sampleRate = 16000; // Use higher sample rate for better precision
            const short amplitude = 8000; // Moderate amplitude to avoid clipping

            for (int i = 0; i < samples; i++) {
                double time = (double)i / sampleRate;
                double sineValue = Math.Sin(2 * Math.PI * frequency * time);
                short sample = (short)(sineValue * amplitude);
                
                // Convert to little-endian bytes
                pcmData[i * 2] = (byte)(sample & 0xFF);
                pcmData[i * 2 + 1] = (byte)((sample >> 8) & 0xFF);
            }

            return pcmData;
        }

        private bool DetectAudioArtifacts(byte[] decodedAudio, byte[] originalAudio) {
            if (decodedAudio.Length < 4 || originalAudio.Length < 4) {
                return false; // Too small to analyze
            }

            try {
                // Convert to 16-bit samples for analysis
                var decodedSamples = new short[decodedAudio.Length / 2];
                var originalSamples = new short[Math.Min(originalAudio.Length / 2, decodedSamples.Length)];

                for (int i = 0; i < decodedSamples.Length; i++) {
                    decodedSamples[i] = BitConverter.ToInt16(decodedAudio, i * 2);
                }

                for (int i = 0; i < originalSamples.Length; i++) {
                    originalSamples[i] = BitConverter.ToInt16(originalAudio, i * 2);
                }

                // Check for excessive noise (high-frequency artifacts)
                int noiseCount = 0;
                const short noiseThreshold = 5000;

                for (int i = 1; i < decodedSamples.Length - 1; i++) {
                    // Look for sudden spikes that indicate noise
                    short current = decodedSamples[i];
                    short prev = decodedSamples[i - 1];
                    short next = decodedSamples[i + 1];

                    if (Math.Abs(current - prev) > noiseThreshold && Math.Abs(current - next) > noiseThreshold) {
                        noiseCount++;
                    }
                }

                // If more than 30% of samples show noise artifacts, consider it problematic
                double noiseRatio = (double)noiseCount / decodedSamples.Length;
                return noiseRatio > 0.3;

            } catch (Exception ex) {
                _logger.LogWarning("Error detecting audio artifacts: {Error}", ex.Message);
                return false; // Assume no artifacts if we can't analyze
            }
        }

        private void UpdateHealthResult(AudioCodec codecType, CodecHealthResult result) {
            var previousResult = _healthResults.TryGetValue(codecType, out var prev) ? prev : null;
            _healthResults[codecType] = result;

            // Notify if health status changed
            if (previousResult == null || previousResult.IsHealthy != result.IsHealthy) {
                _logger.LogInformation("🔄 Codec {Codec} health status changed: {PreviousStatus} -> {NewStatus}", 
                    codecType, 
                    previousResult?.IsHealthy.ToString() ?? "Unknown", 
                    result.IsHealthy);

                // Update codec factory health status
                if (result.IsHealthy) {
                    _codecFactory.MarkCodecHealthy(codecType);
                } else {
                    _codecFactory.MarkCodecUnhealthy(codecType, result.Issue ?? "Health check failed");
                }

                // Notify subscribers
                CodecHealthChanged?.Invoke(codecType, result);
            }
        }

        public void Dispose() {
            if (_disposed) return;

            _disposed = true;
            
            try {
                _healthCheckTimer?.Dispose();
                CodecHealthChanged = null;
                
                _logger.LogInformation("CodecHealthMonitor disposed");
            } catch (Exception ex) {
                _logger.LogError(ex, "Error disposing CodecHealthMonitor");
            }
            
            GC.SuppressFinalize(this);
        }
    }
}