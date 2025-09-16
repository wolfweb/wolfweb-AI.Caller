using System;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Processing {
    public sealed class SimpleProcessingService : IAudioProcessingService {
        public short[] ResampleMonoS16(ReadOnlySpan<short> input, int inRate, int outRate) {
            if (input.Length == 0) return Array.Empty<short>();
            if (inRate == outRate) return input.ToArray();

            double ratio = (double)outRate / inRate;
            int outLen = (int)Math.Round(input.Length * ratio);
            if (outLen <= 0) return Array.Empty<short>();

            var output = new short[outLen];
            for (int i = 0; i < outLen; i++) {
                double srcPos = i / ratio;
                int i0 = (int)Math.Floor(srcPos);
                int i1 = Math.Min(i0 + 1, input.Length - 1);
                double t = srcPos - i0;
                int v = (int)Math.Round((1 - t) * input[i0] + t * input[i1]);
                output[i] = (short)Math.Clamp(v, short.MinValue, short.MaxValue);
            }
            return output;
        }
    }
}