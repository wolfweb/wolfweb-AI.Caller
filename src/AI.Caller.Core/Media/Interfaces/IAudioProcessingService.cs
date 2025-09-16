using System;

namespace AI.Caller.Core.Media.Interfaces {
    // Minimal processing capabilities needed for P1 to be runnable.
    public interface IAudioProcessingService {
        // Resample mono s16 PCM to target sample rate.
        // If inRate == outRate, should return the original array (or a copy).
        short[] ResampleMonoS16(ReadOnlySpan<short> input, int inRate, int outRate);
    }
}