using System;

namespace AI.Caller.Core.Media.Vad {
    public sealed class DefaultVad : IVoiceActivityDetector, IDisposable {
        public void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs) {
            
        }

        public void Dispose() {
            throw new NotImplementedException();
        }

        public VADResult Update(byte[] pcmBytes) {
            throw new NotImplementedException();
        }
    }
}