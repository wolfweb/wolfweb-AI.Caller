using AI.Caller.Core.Media.Vad;

namespace AI.Caller.Core.Media {
    public interface IVoiceActivityDetector {
        void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs);
        VADResult Update(byte[] pcmBytes);
    }
}