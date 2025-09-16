using System;

namespace AI.Caller.Core.Media.Interfaces {
    public interface IAudioEncoder {
        byte[] EncodeMuLaw(ReadOnlySpan<short> pcm);
        byte[] EncodeALaw(ReadOnlySpan<short> pcm);
    }
}