using System;

namespace AI.Caller.Core.Media.Interfaces {
    public interface IAudioDecoder {
        byte[] DecodeG711MuLaw(ReadOnlySpan<byte> payload);
        byte[] DecodeG711ALaw(ReadOnlySpan<byte> payload);
    }
}