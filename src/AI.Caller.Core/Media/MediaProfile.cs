using System;

namespace AI.Caller.Core {
    public enum AudioCodec {
        PCMU = 0, // payload type 0
        PCMA = 8  // payload type 8
    }

    public sealed class MediaProfile {
        public AudioCodec Codec { get; init; } = AudioCodec.PCMU;
        public int PayloadType { get; init; } = 0; // default PT=0 for PCMU
        public int SampleRate { get; init; } = 8000; // Hz
        public int PtimeMs { get; init; } = 20; // packetization time
        public int Channels { get; init; } = 1;

        public int SamplesPerFrame => (SampleRate * PtimeMs) / 1000;

        public MediaProfile() { }

        public MediaProfile(AudioCodec codec, int payloadType, int sampleRate = 8000, int ptimeMs = 20, int channels = 1) {
            Codec = codec;
            PayloadType = payloadType;
            SampleRate = sampleRate;
            PtimeMs = ptimeMs;
            Channels = channels;
        }
    }
}