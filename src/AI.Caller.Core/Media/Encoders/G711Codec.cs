using System;
using AI.Caller.Core.Media.Interfaces;

namespace AI.Caller.Core.Media.Encoders {
    public sealed class G711Codec : IAudioEncoder, IAudioDecoder {
        public byte[] EncodeMuLaw(ReadOnlySpan<byte> pcmBytes) => MuLawEncode(pcmBytes);
        public byte[] EncodeALaw(ReadOnlySpan<byte> pcmBytes) => ALawEncode(pcmBytes);
        public byte[] DecodeG711MuLaw(ReadOnlySpan<byte> payload) => MuLawDecode(payload);
        public byte[] DecodeG711ALaw(ReadOnlySpan<byte> payload) => ALawDecode(payload);
        
        #region MuLaw
        private static byte LinearToMuLawSample(byte lowByte, byte highByte) {
            short sample = (short)(lowByte | (highByte << 8));
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

        private static void MuLawToLinearSample(byte muLaw, out byte lowByte, out byte highByte) {
            muLaw = (byte)~muLaw;
            int sign = muLaw & 0x80;
            int exponent = (muLaw & 0x70) >> 4;
            int mantissa = muLaw & 0x0F;
            int sample = ((mantissa << 3) + 0x84) << exponent;
            sample -= 0x84;
            short result = (short)(sign != 0 ? -sample : sample);
            lowByte = (byte)(result & 0xFF);
            highByte = (byte)((result >> 8) & 0xFF);
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

        private static byte[] MuLawEncode(ReadOnlySpan<byte> pcmBytes) {
            if (pcmBytes.Length % 2 != 0) {
                throw new ArgumentException("PCM byte array length must be even for 16-bit audio");
            }
            var output = new byte[pcmBytes.Length / 2];
            for (int i = 0; i < output.Length; i++) {
                output[i] = LinearToMuLawSample(pcmBytes[i * 2], pcmBytes[i * 2 + 1]);
            }
            return output;
        }

        private static byte[] MuLawDecode(ReadOnlySpan<byte> data) {
            var output = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++) {
                MuLawToLinearSample(data[i], out byte lowByte, out byte highByte);
                output[i * 2] = lowByte;
                output[i * 2 + 1] = highByte;
            }
            return output;
        }
        #endregion

        #region ALaw
        private static byte LinearToALawSample(byte lowByte, byte highByte) {
            short sample = (short)(lowByte | (highByte << 8));
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

        private static void ALawToLinearSample(byte aLaw, out byte lowByte, out byte highByte) {
            aLaw ^= 0xD5;
            int sign = aLaw & 0x80;
            int exponent = (aLaw & 0x70) >> 4;
            int mantissa = aLaw & 0x0F;
            int sample = (mantissa << 4) + 8;
            if (exponent != 0) sample = (sample + 0x100) << (exponent - 1);
            short result = (short)(sign != 0 ? -sample : sample);
            lowByte = (byte)(result & 0xFF);
            highByte = (byte)((result >> 8) & 0xFF);
        }

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

        private static byte[] ALawEncode(ReadOnlySpan<byte> pcmBytes) {
            if (pcmBytes.Length % 2 != 0) {
                throw new ArgumentException("PCM byte array length must be even for 16-bit audio");
            }
            var output = new byte[pcmBytes.Length / 2];
            for (int i = 0; i < output.Length; i++) {
                output[i] = LinearToALawSample(pcmBytes[i * 2], pcmBytes[i * 2 + 1]);
            }
            return output;
        }

        private static byte[] ALawDecode(ReadOnlySpan<byte> data) {
            var output = new byte[data.Length * 2];
            for (int i = 0; i < data.Length; i++) {
                ALawToLinearSample(data[i], out byte lowByte, out byte highByte);
                output[i * 2] = lowByte;
                output[i * 2 + 1] = highByte;
            }
            return output;
        }
        #endregion
    }
}