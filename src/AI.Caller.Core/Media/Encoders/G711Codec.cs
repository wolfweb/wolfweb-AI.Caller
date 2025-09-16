using System;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Encoders {
    public sealed class G711Codec : IAudioEncoder, IAudioDecoder {
        public byte[] EncodeMuLaw(ReadOnlySpan<short> pcm) => MuLawEncode(pcm);
        public byte[] EncodeALaw(ReadOnlySpan<short> pcm) => ALawEncode(pcm);
        public short[] DecodeG711MuLaw(ReadOnlySpan<byte> payload) => MuLawDecode(payload);
        public short[] DecodeG711ALaw(ReadOnlySpan<byte> payload) => ALawDecode(payload);

        #region MuLaw
        private static byte LinearToMuLawSample(short sample) {
            const int cBias = 0x84;
            const int cClip = 32635;

            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = (short)-sample;
            if (sample > cClip) sample = cClip;

            sample = (short)(sample + cBias);
            int exponent = MuLawExponentTable[(sample >> 7) & 0xFF];
            int mantissa = (sample >> (exponent + 3)) & 0x0F;
            byte muLaw = (byte)~(sign | (exponent << 4) | mantissa);
            return muLaw;
        }

        private static short MuLawToLinearSample(byte muLaw) {
            muLaw = (byte)~muLaw;
            int sign = muLaw & 0x80;
            int exponent = (muLaw & 0x70) >> 4;
            int mantissa = muLaw & 0x0F;
            int sample = ((mantissa << 3) + 0x84) << exponent;
            sample -= 0x84;
            return (short)(sign != 0 ? -sample : sample);
        }

        private static readonly byte[] MuLawExponentTable = new byte[256];

        static G711Codec() {
            for (int i = 0; i < 256; i++) {
                int val = i << 3;
                int exp = 7;
                while (exp > 0 && (val & (1 << (exp + 3))) == 0) exp--;
                MuLawExponentTable[i] = (byte)exp;
            }
        }

        private static byte[] MuLawEncode(ReadOnlySpan<short> pcm) {
            var output = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                output[i] = LinearToMuLawSample(pcm[i]);
            return output;
        }

        private static short[] MuLawDecode(ReadOnlySpan<byte> data) {
            var output = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
                output[i] = MuLawToLinearSample(data[i]);
            return output;
        }
        #endregion

        #region ALaw
        private static byte LinearToALawSample(short sample) {
            int sign = (sample >> 8) & 0x80;
            if (sign != 0) sample = (short)-sample;
            if (sample > 32635) sample = 32635;

            int exponent;
            int mantissa;
            if (sample >= 256) {
                exponent = ALawExponentTable[(sample >> 8) & 0x7F];
                mantissa = (sample >> (exponent + 3)) & 0x0F;
            } else {
                exponent = 0;
                mantissa = (sample >> 4) & 0x0F;
            }

            byte aLaw = (byte)(sign | (exponent << 4) | mantissa);
            return (byte)(aLaw ^ 0xD5);
        }

        private static short ALawToLinearSample(byte aLaw) {
            aLaw ^= 0xD5;
            int sign = aLaw & 0x80;
            int exponent = (aLaw & 0x70) >> 4;
            int mantissa = aLaw & 0x0F;
            int sample = (mantissa << 4) + 8;
            if (exponent != 0) sample = (sample + 0x100) << (exponent - 1);
            return (short)(sign != 0 ? -sample : sample);
        }

        // 标准 A-law 指数查找表（128 项）
        // 标准 A-law 指数查找表（128 项）
        private static readonly byte[] ALawExponentTable = new byte[128]
        {
            0,0,1,1,2,2,2,2,
            3,3,3,3,3,3,3,3,
            4,4,4,4,4,4,4,4,
            4,4,4,4,4,4,4,4,
            5,5,5,5,5,5,5,5,
            5,5,5,5,5,5,5,5,
            5,5,5,5,5,5,5,5,
            5,5,5,5,5,5,5,5,
            6,6,6,6,6,6,6,6,
            6,6,6,6,6,6,6,6,
            6,6,6,6,6,6,6,6,
            6,6,6,6,6,6,6,6,
            7,7,7,7,7,7,7,7,
            7,7,7,7,7,7,7,7,
            7,7,7,7,7,7,7,7,
            7,7,7,7,7,7,7,7
        };

        private static byte[] ALawEncode(ReadOnlySpan<short> pcm) {
            var output = new byte[pcm.Length];
            for (int i = 0; i < pcm.Length; i++)
                output[i] = LinearToALawSample(pcm[i]);
            return output;
        }

        private static short[] ALawDecode(ReadOnlySpan<byte> data) {
            var output = new short[data.Length];
            for (int i = 0; i < data.Length; i++)
                output[i] = ALawToLinearSample(data[i]);
            return output;
        }
        #endregion
    }
}