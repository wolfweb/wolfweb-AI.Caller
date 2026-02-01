using AI.Caller.Core.Media.Interfaces;
using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;

namespace AI.Caller.Core.Media.Encoders {
    /// <summary>
    /// G.711编解码器
    /// 支持A-Law和μ-Law编码/解码
    /// </summary>
    public sealed class G711Codec : IAudioCodec {
        private readonly ILogger _logger;
        private readonly AudioCodec _codecType;

        public AudioCodec Type => _codecType;
        public int SampleRate { get; }
        public int Channels { get; }

        public G711Codec(ILogger<G711Codec> logger, AudioCodec codecType = AudioCodec.PCMA, int sampleRate = 8000, int channels = 1) {
            _logger = logger;
            _codecType = codecType;
            SampleRate = sampleRate;
            Channels = channels;


            _logger.LogInformation("G711Codec (Pure C# / LUT Optimized) initialized: Type={Type}", _codecType);
        }

        public byte[] Encode(ReadOnlySpan<byte> pcm16) {
            byte[] encoded = new byte[pcm16.Length / 2];
            ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(pcm16);

            if (_codecType == AudioCodec.PCMA) {
                for (int i = 0; i < samples.Length; i++) {
                    encoded[i] = ALawEncoder.LinearToALawSample(samples[i]);
                }
            } else {
                for (int i = 0; i < samples.Length; i++) {
                    encoded[i] = MuLawEncoder.LinearToMuLawSample(samples[i]);
                }
            }
            return encoded;
        }

        public int Decode(ReadOnlySpan<byte> encoded, Span<byte> decodedOutput) {
            if (decodedOutput.Length < encoded.Length * 2) throw new ArgumentException("Output buffer too small");
            Span<short> samples = MemoryMarshal.Cast<byte, short>(decodedOutput);

            if (_codecType == AudioCodec.PCMA) {
                for (int i = 0; i < encoded.Length; i++) {
                    samples[i] = ALawDecoder.ALawToLinearSample(encoded[i]);
                }
            } else {
                for (int i = 0; i < encoded.Length; i++) {
                    samples[i] = MuLawDecoder.MuLawToLinearSample(encoded[i]);
                }
            }
            return encoded.Length * 2;
        }

        public byte[] GenerateSilenceFrame(int durationMs) {
            int sampleCount = SampleRate * durationMs / 1000;
            byte silenceByte = _codecType == AudioCodec.PCMA ? (byte)0xD5 : (byte)0xFF;

            byte[] silence = new byte[sampleCount];
            Array.Fill(silence, silenceByte);
            return silence;
        }

        public void Dispose() { }
    }

    // ==========================================
    // 核心算法实现
    // ==========================================

    public static class ALawEncoder {
        private const int MAX = 0xFFF; // 4095

        public static byte LinearToALawSample(short sample) {
            int mask;
            int pcm_val = sample;

            if (pcm_val >= 0) {
                mask = 0xD5;
            } else {
                mask = 0x55;
                pcm_val = -pcm_val; // 取绝对值
            }

            if (pcm_val > MAX) pcm_val = MAX;

            int seg;
            if (pcm_val < 256) seg = 0;
            else if (pcm_val < 512) seg = 1;
            else if (pcm_val < 1024) seg = 2;
            else if (pcm_val < 2048) seg = 3;
            else if (pcm_val < 4096) seg = 4;
            else if (pcm_val < 8192) seg = 5;
            else if (pcm_val < 16384) seg = 6;
            else seg = 7;

            if (seg >= 8) return (byte)(0x7F ^ mask);

            int aval = (seg << 4) | ((pcm_val >> (seg == 0 ? 4 : seg + 3)) & 0x0F);
            return (byte)(aval ^ mask);
        }
    }

    public static class ALawDecoder {
        private static readonly short[] _aLawToLinearTable = new short[256];

        static ALawDecoder() {
            for (int i = 0; i < 256; i++) {
                _aLawToLinearTable[i] = CalculateALawToLinear((byte)i);
            }
        }

        public static short ALawToLinearSample(byte alaw) {
            return _aLawToLinearTable[alaw];
        }

        private static short CalculateALawToLinear(byte alaw) {
            int t = alaw ^ 0x55;

            int seg = (t >> 4) & 0x07;
            int mant = (t & 0x0F);

            int sample = (mant << 4) + 8;

            if (seg > 0) {
                sample += 0x100;
                sample <<= (seg - 1);
            }

            return (short)((t & 0x80) != 0 ? sample : -sample);
        }
    }

    public static class MuLawEncoder {
        private const int BIAS = 0x84;
        private const int CLIP = 32635;

        public static byte LinearToMuLawSample(short sample) {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = (short)-sample;
            if (sample > CLIP) sample = CLIP;

            sample = (short)(sample + BIAS);
            int exponent = 7;
            int expMask = 0x4000;
            for (; (sample & expMask) == 0 && exponent > 0; exponent--, expMask >>= 1) { }

            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            byte mulaw = (byte)(sign | (exponent << 4) | mantissa);
            return (byte)~mulaw;
        }
    }

    public static class MuLawDecoder {
        private static readonly short[] _muLawToLinearTable = new short[256];

        static MuLawDecoder() {
            for (int i = 0; i < 256; i++) {
                _muLawToLinearTable[i] = CalculateMuLawToLinear((byte)i);
            }
        }

        public static short MuLawToLinearSample(byte mulaw) {
            return _muLawToLinearTable[mulaw];
        }

        private static short CalculateMuLawToLinear(byte mulaw) {
            mulaw = (byte)~mulaw;
            int sign = mulaw & 0x80;
            int exponent = (mulaw >> 4) & 0x07;
            int mantissa = mulaw & 0x0F;

            int sample = ((mantissa << 3) + 0x84) << exponent;
            sample -= 0x84;

            return (short)(sign != 0 ? -sample : sample);
        }
    }
}
