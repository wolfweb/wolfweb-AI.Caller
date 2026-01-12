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
}