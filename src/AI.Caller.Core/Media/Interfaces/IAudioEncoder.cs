using System;

namespace AI.Caller.Core.Media.Interfaces {
    public interface IAudioEncoder {
        byte[] EncodeMuLaw(ReadOnlySpan<byte> pcm);
        byte[] EncodeALaw(ReadOnlySpan<byte> pcm);        
    }
}