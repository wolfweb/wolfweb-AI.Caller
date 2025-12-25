using System;

namespace AI.Caller.Core {
    public enum AudioCodec {
        PCMU = 0, // G.711 μ-law, payload type 0
        PCMA = 8, // G.711 A-law, payload type 8
        G722 = 9  // G.722, payload type 9
    }

    public sealed class MediaProfile {
        public AudioCodec Codec { get; init; } = AudioCodec.PCMA;
        public int PayloadType { get; init; } = 0; // default PT=0 for PCMU
        public int SampleRate { get; init; } = 8000; // Hz
        public int PtimeMs { get; init; } = 20; // packetization time
        public int Channels { get; init; } = 1;

        public int SamplesPerFrame => (SampleRate * PtimeMs) / 1000;

        public MediaProfile(AudioCodec codec, int payloadType, int sampleRate = 8000, int ptimeMs = 20, int channels = 1) {
            Codec = codec;
            PayloadType = payloadType;
            SampleRate = sampleRate;
            PtimeMs = ptimeMs;
            Channels = channels;
        }

        // 🔧 OPTIMIZATION: 预定义的标准配置，避免多处 new 和参数错误
        
        /// <summary>
        /// G.711 A-law 标准配置 (8kHz, PT=8)
        /// </summary>
        public static MediaProfile G711ALaw => new(AudioCodec.PCMA, payloadType: 8, sampleRate: 8000, ptimeMs: 20, channels: 1);
        
        /// <summary>
        /// G.711 μ-law 标准配置 (8kHz, PT=0)
        /// </summary>
        public static MediaProfile G711MuLaw => new(AudioCodec.PCMU, payloadType: 0, sampleRate: 8000, ptimeMs: 20, channels: 1);
        
        /// <summary>
        /// G.722 标准配置 (16kHz, PT=9)
        /// </summary>
        public static MediaProfile G722 => new(AudioCodec.G722, payloadType: 9, sampleRate: 16000, ptimeMs: 20, channels: 1);
        
        /// <summary>
        /// 默认配置 (G.711 A-law, 8kHz, PT=8)
        /// </summary>
        public static MediaProfile Default => G711ALaw;

        /// <summary>
        /// 根据协商结果创建 MediaProfile
        /// </summary>
        /// <param name="codec">协商的编解码器</param>
        /// <param name="payloadType">协商的 Payload Type</param>
        /// <param name="sampleRate">协商的采样率</param>
        /// <param name="ptimeMs">打包时间 (默认 20ms)</param>
        /// <param name="channels">声道数 (默认 1)</param>
        /// <returns>对应的 MediaProfile</returns>
        public static MediaProfile FromNegotiation(AudioCodec codec, int payloadType, int sampleRate, int ptimeMs = 20, int channels = 1) {
            return new MediaProfile(codec, payloadType, sampleRate, ptimeMs, channels);
        }

        /// <summary>
        /// 根据编解码器类型创建标准配置
        /// </summary>
        /// <param name="codec">编解码器类型</param>
        /// <returns>对应的标准 MediaProfile</returns>
        public static MediaProfile FromCodec(AudioCodec codec) {
            return codec switch {
                AudioCodec.PCMU => G711MuLaw,
                AudioCodec.PCMA => G711ALaw,
                AudioCodec.G722 => G722,
                _ => Default
            };
        }

        public override string ToString() {
            return $"{Codec}@{SampleRate}Hz (PT:{PayloadType}, {PtimeMs}ms, {Channels}ch)";
        }
    }
}