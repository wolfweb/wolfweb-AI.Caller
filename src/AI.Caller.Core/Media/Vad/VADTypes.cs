namespace AI.Caller.Core.Media.Vad {
    public enum VADState {
        Silence,
        Speaking
    }

    public readonly struct VADResult {
        public VADResult(VADState state, float energy) {
            State = state;
            Energy = energy;
        }

        public VADState State { get; }
        public float Energy { get; }
    }

    public interface IVoiceActivityDetector {
        void Configure(float energyThreshold, int enterSpeakingMs, int resumeSilenceMs, int sampleRate, int frameMs);
        VADResult Update(byte[] pcmBytes);        
    }
}