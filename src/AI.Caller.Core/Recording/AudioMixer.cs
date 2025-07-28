using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording {
    public class AudioMixer : IDisposable {
        private readonly ILogger _logger;
        private readonly object _lockObject = new object();
        private bool _disposed = false;

        public event EventHandler<AudioFrame>? MixedAudioReady;

        public AudioMixer(ILogger logger) {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public AudioFrame? MixFrames(IEnumerable<AudioFrame> frames) {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioMixer));

            var frameList = frames.ToList();
            if (!frameList.Any())
                return null;

            try {
                if (frameList.Count == 1) {
                    var singleFrame = frameList[0];
                    return new AudioFrame(singleFrame.Data, singleFrame.Format, AudioSource.Mixed) {
                        SequenceNumber = singleFrame.SequenceNumber,
                        Timestamp = singleFrame.Timestamp
                    };
                }

                var referenceFormat = frameList[0].Format;
                if (!frameList.All(f => f.Format.IsCompatibleWith(referenceFormat))) {
                    _logger.LogWarning("Cannot mix audio frames with incompatible formats");
                    return null;
                }

                var minLength = frameList.Min(f => f.Data.Length);
                var mixedData = new byte[minLength];

                MixAudioData(frameList, mixedData, referenceFormat);

                var mixedFrame = new AudioFrame(mixedData, referenceFormat, AudioSource.Mixed) {
                    SequenceNumber = frameList.Max(f => f.SequenceNumber),
                    Timestamp = frameList.Max(f => f.Timestamp)
                };

                _logger.LogTrace($"Mixed {frameList.Count} audio frames into {mixedData.Length} bytes");
                return mixedFrame;
            } catch (Exception ex) {
                _logger.LogError(ex, $"Error mixing audio frames: {ex.Message}");
                return null;
            }
        }

        public AudioFrame? MixFrames(AudioFrame frame1, AudioFrame frame2) {
            return MixFrames(new[] { frame1, frame2 });
        }

        private void MixAudioData(List<AudioFrame> frames, byte[] output, AudioFormat format) {
            switch (format.SampleFormat) {
                case AudioSampleFormat.PCM:
                    MixPcmData(frames, output, format);
                    break;

                case AudioSampleFormat.ALAW:
                    MixAlawData(frames, output);
                    break;

                case AudioSampleFormat.ULAW:
                    MixUlawData(frames, output);
                    break;

                default:
                    MixByteAveraging(frames, output);
                    break;
            }
        }

        private void MixPcmData(List<AudioFrame> frames, byte[] output, AudioFormat format) {
            var sampleSize = format.BitsPerSample / 8;
            var sampleCount = output.Length / sampleSize;

            for (int i = 0; i < sampleCount; i++) {
                int mixedSample = 0;
                int validFrames = 0;

                foreach (var frame in frames) {
                    if (i * sampleSize + sampleSize <= frame.Data.Length) {
                        var sample = ExtractPcmSample(frame.Data, i * sampleSize, sampleSize);
                        mixedSample += sample;
                        validFrames++;
                    }
                }

                if (validFrames > 0) {
                    mixedSample = Math.Max(Math.Min(mixedSample / validFrames, GetMaxSampleValue(sampleSize)), GetMinSampleValue(sampleSize));
                    WritePcmSample(output, i * sampleSize, mixedSample, sampleSize);
                }
            }
        }

        private void MixAlawData(List<AudioFrame> frames, byte[] output) {
            for (int i = 0; i < output.Length; i++) {
                int mixedValue = 0;
                int validFrames = 0;

                foreach (var frame in frames) {
                    if (i < frame.Data.Length) {
                        var linearValue = AlawToLinear(frame.Data[i]);
                        mixedValue += linearValue;
                        validFrames++;
                    }
                }

                if (validFrames > 0) {
                    mixedValue /= validFrames;
                    output[i] = LinearToAlaw((short)Math.Max(Math.Min(mixedValue, short.MaxValue), short.MinValue));
                }
            }
        }

        private void MixUlawData(List<AudioFrame> frames, byte[] output) {
            for (int i = 0; i < output.Length; i++) {
                int mixedValue = 0;
                int validFrames = 0;

                foreach (var frame in frames) {
                    if (i < frame.Data.Length) {
                        var linearValue = UlawToLinear(frame.Data[i]);
                        mixedValue += linearValue;
                        validFrames++;
                    }
                }

                if (validFrames > 0) {
                    mixedValue /= validFrames;
                    output[i] = LinearToUlaw((short)Math.Max(Math.Min(mixedValue, short.MaxValue), short.MinValue));
                }
            }
        }

        private void MixByteAveraging(List<AudioFrame> frames, byte[] output) {
            for (int i = 0; i < output.Length; i++) {
                int sum = 0;
                int count = 0;

                foreach (var frame in frames) {
                    if (i < frame.Data.Length) {
                        sum += frame.Data[i];
                        count++;
                    }
                }

                output[i] = count > 0 ? (byte)(sum / count) : (byte)0;
            }
        }

        private int ExtractPcmSample(byte[] data, int offset, int sampleSize) {
            return sampleSize switch {
                1 => (sbyte)data[offset],
                2 => BitConverter.ToInt16(data, offset),
                4 => BitConverter.ToInt32(data, offset),
                _ => 0
            };
        }

        private void WritePcmSample(byte[] data, int offset, int sample, int sampleSize) {
            switch (sampleSize) {
                case 1:
                    data[offset] = (byte)(sbyte)sample;
                    break;
                case 2:
                    BitConverter.GetBytes((short)sample).CopyTo(data, offset);
                    break;
                case 4:
                    BitConverter.GetBytes(sample).CopyTo(data, offset);
                    break;
            }
        }

        private int GetMaxSampleValue(int sampleSize) {
            return sampleSize switch {
                1 => sbyte.MaxValue,
                2 => short.MaxValue,
                4 => int.MaxValue,
                _ => 0
            };
        }

        private int GetMinSampleValue(int sampleSize) {
            return sampleSize switch {
                1 => sbyte.MinValue,
                2 => short.MinValue,
                4 => int.MinValue,
                _ => 0
            };
        }

        private short AlawToLinear(byte alaw) {
            alaw ^= 0x55;
            int sign = alaw & 0x80;
            int exponent = (alaw & 0x70) >> 4;
            int mantissa = alaw & 0x0F;

            int sample = mantissa << 4;
            if (exponent != 0)
                sample += 0x100;
            if (exponent > 1)
                sample <<= exponent - 1;

            return (short)(sign != 0 ? -sample : sample);
        }

        private byte LinearToAlaw(short linear) {
            int sign = (linear & 0x8000) >> 8;
            if (sign != 0)
                linear = (short)-linear;

            if (linear > 32635)
                linear = 32635;

            int exponent = 7;
            int mantissa;

            for (int i = 0x4000; i != 0; i >>= 1, exponent--) {
                if ((linear & i) != 0)
                    break;
            }

            mantissa = (linear >> (exponent > 0 ? exponent + 3 : 4)) & 0x0F;
            return (byte)(sign | (exponent << 4) | mantissa ^ 0x55);
        }

        private short UlawToLinear(byte ulaw) {
            ulaw = (byte)~ulaw;
            int sign = ulaw & 0x80;
            int exponent = (ulaw & 0x70) >> 4;
            int mantissa = ulaw & 0x0F;

            int sample = ((mantissa << 3) + 0x84) << exponent;
            return (short)(sign != 0 ? -sample : sample);
        }

        private byte LinearToUlaw(short linear) {
            int sign = (linear & 0x8000) >> 8;
            if (sign != 0)
                linear = (short)-linear;

            if (linear > 32635)
                linear = 32635;

            int exponent = 7;
            int mantissa;

            for (int i = 0x4000; i != 0; i >>= 1, exponent--) {
                if ((linear & i) != 0)
                    break;
            }

            mantissa = (linear >> (exponent > 0 ? exponent + 3 : 4)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }

        public void Dispose() {
            if (_disposed)
                return;

            lock (_lockObject) {
                if (_disposed)
                    return;

                _disposed = true;
                _logger.LogInformation("AudioMixer disposed");
            }
        }
    }
}