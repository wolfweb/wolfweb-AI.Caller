using AI.Caller.Core.Media.Encoders;
using AI.Caller.Core.Media.Interfaces;
using AI.Caller.Core.Network;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Media {
    /// <summary>
    /// Codec negotiation result containing selected codec and fallback information
    /// </summary>
    public class CodecNegotiationResult {
        public AudioCodec SelectedCodec { get; set; }
        public AudioCodec FallbackCodec { get; set; }
        public string NegotiationReason { get; set; } = string.Empty;
        public int SelectedPayloadType { get; set; }
        public int SelectedSampleRate { get; set; }
        public bool IsUsingFallback { get; set; }
        
        public override string ToString() {
            return $"Selected: {SelectedCodec}@{SelectedSampleRate}Hz (PT:{SelectedPayloadType}), " +
                   $"Fallback: {FallbackCodec}, Reason: {NegotiationReason}, UsingFallback: {IsUsingFallback}";
        }
    }

    /// <summary>
    /// Factory for creating audio codecs based on negotiated media type with adaptive capabilities
    /// </summary>
    public class AudioCodecFactory {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<AudioCodecFactory> _logger;
        private readonly Dictionary<AudioCodec, bool> _codecHealthStatus = new();
        private readonly object _healthLock = new object();

        public AudioCodecFactory(IServiceProvider serviceProvider, ILogger<AudioCodecFactory> logger) {
            _serviceProvider = serviceProvider;
            _logger = logger;
            
            _logger.LogInformation("AudioCodecFactory initialized with enhanced G.722 support");
            
            // Initialize codec health status
            InitializeCodecHealthStatus();
        }

        /// <summary>
        /// Negotiate the best codec based on supported codecs and network quality
        /// </summary>
        public CodecNegotiationResult NegotiateCodec(
            List<AudioCodec> supportedCodecs, 
            NetworkQuality networkQuality = NetworkQuality.Good,
            bool preferHighQuality = true) {
            
            _logger.LogInformation("🔄 Negotiating codec: SupportedCodecs=[{SupportedCodecs}], NetworkQuality={NetworkQuality}, PreferHighQuality={PreferHighQuality}", string.Join(", ", supportedCodecs), networkQuality, preferHighQuality);

            var result = new CodecNegotiationResult();
            
            // Define codec preference order based on quality and network conditions
            var preferredOrder = GetCodecPreferenceOrder(networkQuality, preferHighQuality);
            
            // Find the best available codec
            AudioCodec? selectedCodec = null;
            AudioCodec? fallbackCodec = null;
            
            foreach (var preferredCodec in preferredOrder) {
                if (supportedCodecs.Contains(preferredCodec)) {
                    if (IsCodecHealthy(preferredCodec)) {
                        if (selectedCodec == null) {
                            selectedCodec = preferredCodec;
                        } else if (fallbackCodec == null) {
                            fallbackCodec = preferredCodec;
                            break; // We have both primary and fallback
                        }
                    } else {
                        _logger.LogWarning("⚠️ Codec {Codec} is not healthy, skipping", preferredCodec);
                    }
                }
            }
            
            // If no healthy codec found, use any available codec as last resort
            if (selectedCodec == null) {
                selectedCodec = supportedCodecs.FirstOrDefault();
                _logger.LogWarning("⚠️ No healthy codec found, using first available: {Codec}", selectedCodec);
                result.NegotiationReason = "No healthy codec available, using first supported codec";
            }
            
            // Set fallback if not found
            if (fallbackCodec == null && supportedCodecs.Count > 1) {
                fallbackCodec = supportedCodecs.FirstOrDefault(c => c != selectedCodec);
            }
            
            // Populate result
            if (selectedCodec.HasValue) {
                result.SelectedCodec = selectedCodec.Value;
                result.SelectedPayloadType = GetPayloadTypeForCodec(selectedCodec.Value);
                result.SelectedSampleRate = GetSampleRateForCodec(selectedCodec.Value);
                
                if (fallbackCodec.HasValue) {
                    result.FallbackCodec = fallbackCodec.Value;
                }
                
                if (string.IsNullOrEmpty(result.NegotiationReason)) {
                    result.NegotiationReason = $"Selected based on network quality ({networkQuality}) and codec health";
                }
                
                _logger.LogInformation("✅ Codec negotiation result: {Result}", result);
            } else {
                throw new InvalidOperationException("No supported codecs available for negotiation");
            }
            
            return result;
        }

        /// <summary>
        /// Check if a codec is healthy and working properly
        /// </summary>
        public bool IsCodecHealthy(AudioCodec codecType) {
            lock (_healthLock) {
                if (_codecHealthStatus.TryGetValue(codecType, out bool isHealthy)) {
                    return isHealthy;
                }
                
                // If not tested yet, assume healthy
                return true;
            }
        }

        /// <summary>
        /// Mark a codec as unhealthy
        /// </summary>
        public void MarkCodecUnhealthy(AudioCodec codecType, string reason) {
            lock (_healthLock) {
                _codecHealthStatus[codecType] = false;
                _logger.LogWarning("❌ Codec {Codec} marked as unhealthy: {Reason}", codecType, reason);
            }
        }

        /// <summary>
        /// Mark a codec as healthy
        /// </summary>
        public void MarkCodecHealthy(AudioCodec codecType) {
            lock (_healthLock) {
                _codecHealthStatus[codecType] = true;
                _logger.LogInformation("✅ Codec {Codec} marked as healthy", codecType);
            }
        }

        /// <summary>
        /// Reset all codec health status
        /// </summary>
        public void ResetCodecHealthStatus() {
            lock (_healthLock) {
                InitializeCodecHealthStatus();
                _logger.LogInformation("🔄 Codec health status reset");
            }
        }

        private void InitializeCodecHealthStatus() {
            _codecHealthStatus[AudioCodec.PCMA] = true;
            _codecHealthStatus[AudioCodec.PCMU] = true;
            _codecHealthStatus[AudioCodec.G722] = true;
        }

        private List<AudioCodec> GetCodecPreferenceOrder(NetworkQuality networkQuality, bool preferHighQuality) {
            var order = new List<AudioCodec>();
            
            if (preferHighQuality && networkQuality >= NetworkQuality.Fair) {
                // Prefer G.722 for better quality when network allows
                order.Add(AudioCodec.G722);
                order.Add(AudioCodec.PCMA);
                order.Add(AudioCodec.PCMU);
            } else {
                // Prefer G.711 for poor network conditions
                order.Add(AudioCodec.PCMA);
                order.Add(AudioCodec.PCMU);
                if (networkQuality >= NetworkQuality.Fair) {
                    order.Add(AudioCodec.G722);
                }
            }
            
            return order;
        }

        private int GetPayloadTypeForCodec(AudioCodec codec) {
            return codec switch {
                AudioCodec.PCMU => 0,
                AudioCodec.PCMA => 8,
                AudioCodec.G722 => 9,
                _ => 8 // Default to PCMA
            };
        }

        private int GetSampleRateForCodec(AudioCodec codec) {
            return codec switch {
                AudioCodec.PCMU => 8000,
                AudioCodec.PCMA => 8000,
                AudioCodec.G722 => 16000,
                _ => 8000 // Default to 8kHz
            };
        }

        public IAudioCodec GetCodec(AudioCodec codecType) {
            _logger.LogInformation("Creating audio codec for type: {CodecType}", codecType);

            switch (codecType) {
                case AudioCodec.PCMA:
                case AudioCodec.PCMU:
                    // G.711 is 8kHz
                    return ActivatorUtilities.CreateInstance<G711Codec>(_serviceProvider, codecType, 8000, 1);
                
                case AudioCodec.G722:
                    // G.722 is 16kHz - use enhanced FFmpeg implementation
                    _logger.LogInformation("Using enhanced G722Codec (FFmpeg with quality improvements)");
                    return ActivatorUtilities.CreateInstance<G722Codec>(_serviceProvider, 1);

                default:
                    throw new NotSupportedException($"Audio codec {codecType} is not supported.");
            }
        }
        
        /// <summary>
        /// Get current G.722 implementation status (always true - using enhanced FFmpeg implementation)
        /// </summary>
        public bool IsUsingNativeG722 => true;
    }
}