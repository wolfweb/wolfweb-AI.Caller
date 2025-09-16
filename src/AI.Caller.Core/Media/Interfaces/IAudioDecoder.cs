using System;

namespace AI.Caller.Core.Media.Interfaces {
    public interface IAudioDecoder {
        short[] DecodeG711MuLaw(ReadOnlySpan<byte> payload);
        short[] DecodeG711ALaw(ReadOnlySpan<byte> payload);
    }
}