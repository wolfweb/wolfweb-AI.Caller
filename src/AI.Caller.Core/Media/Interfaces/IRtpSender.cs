using System;

namespace AI.Caller.Core.Media.Interfaces {
    public interface IRtpSender {
        void SendAudioFrame(ReadOnlySpan<byte> payload, uint rtpTimestamp, int payloadType);
    }
}