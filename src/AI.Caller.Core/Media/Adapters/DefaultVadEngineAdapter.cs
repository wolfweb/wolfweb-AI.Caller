using AI.Caller.Core.Media.Vad;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SherpaOnnx;

namespace AI.Caller.Core.Media.Adapters {
    public sealed class DefaultVadEngineAdapter : IVoiceActivityDetector, IDisposable {
        private readonly VoiceActivityDetector _vad;
        private readonly AudioResamplerCrossType<byte, float> _resampler;
        public DefaultVadEngineAdapter(IOptions<VadSettings> vadOption, ILogger<IVoiceActivityDetector> logger) {
            var vadModelConfig = new VadModelConfig();
            vadModelConfig.Debug                     = vadOption.Value.Debug;
            vadModelConfig.TenVad.Model              = Path.Combine(vadOption.Value.ModelFolder, vadOption.Value.ModelFile);
            vadModelConfig.TenVad.Threshold          = vadOption.Value.Threshold;
            vadModelConfig.TenVad.WindowSize         = vadOption.Value.WindowSize;
            vadModelConfig.TenVad.MinSilenceDuration = vadOption.Value.MinSilenceDuration;
            vadModelConfig.TenVad.MinSpeechDuration  = vadOption.Value.MinSpeechDuration;
            vadModelConfig.TenVad.MaxSpeechDuration  = vadOption.Value.MaxSpeechDuration;

            _vad = new VoiceActivityDetector(vadModelConfig, 60);
            _resampler = new AudioResamplerCrossType<byte, float>(8000, 8000, logger);
        }
        public void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs) {
            
        }

        public VADResult Update(byte[] pcmBytes) {
            _vad.AcceptWaveform(_resampler.Resample(pcmBytes));
            if (_vad.IsSpeechDetected()) {
                _vad.Pop();
                _vad.Flush();
                return new VADResult(VADState.Speaking, 0);
            }
            _vad.Flush();
            return new VADResult(VADState.Silence, 0);
        }
        public void Dispose() {
            _vad.Dispose();
        }
    }
}