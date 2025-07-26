using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    public class AudioFormatConverter : IDisposable
    {
        private readonly ILogger _logger;
        private bool _disposed = false;
        
        public AudioFormatConverter(ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        
        public AudioFrame? ConvertFormat(AudioFrame inputFrame, AudioFormat targetFormat)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioFormatConverter));
                
            if (inputFrame?.Data == null || inputFrame.Data.Length == 0)
                return null;
                
            try
            {
                if (inputFrame.Format.IsCompatibleWith(targetFormat))
                {
                    return inputFrame;
                }
                
                var convertedData = ConvertAudioData(inputFrame.Data, inputFrame.Format, targetFormat);
                if (convertedData == null)
                    return null;
                
                return new AudioFrame(convertedData, targetFormat, inputFrame.Source)
                {
                    SequenceNumber = inputFrame.SequenceNumber,
                    Timestamp = inputFrame.Timestamp
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting audio format: {ex.Message}");
                return null;
            }
        }
        
        public byte[]? ResampleAudio(byte[] inputData, int inputSampleRate, int outputSampleRate, int channels, int bitsPerSample)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioFormatConverter));
                
            if (inputData == null || inputData.Length == 0)
                return null;
                
            if (inputSampleRate == outputSampleRate)
                return inputData;
                
            try
            {
                var sampleSize = bitsPerSample / 8;
                var inputSampleCount = inputData.Length / (sampleSize * channels);
                var outputSampleCount = (int)((long)inputSampleCount * outputSampleRate / inputSampleRate);
                var outputData = new byte[outputSampleCount * sampleSize * channels];
                
                // 简单的线性插值重采样
                for (int outputSample = 0; outputSample < outputSampleCount; outputSample++)
                {
                    var inputPosition = (double)outputSample * inputSampleRate / outputSampleRate;
                    var inputSample = (int)inputPosition;
                    var fraction = inputPosition - inputSample;
                    
                    for (int channel = 0; channel < channels; channel++)
                    {
                        var sample1 = ExtractSample(inputData, inputSample, channel, channels, sampleSize);
                        var sample2 = ExtractSample(inputData, Math.Min(inputSample + 1, inputSampleCount - 1), channel, channels, sampleSize);
                        
                        var interpolatedSample = (int)(sample1 + (sample2 - sample1) * fraction);
                        WriteSample(outputData, outputSample, channel, channels, sampleSize, interpolatedSample);
                    }
                }
                
                _logger.LogTrace($"Resampled audio: {inputSampleRate}Hz -> {outputSampleRate}Hz, {inputData.Length} -> {outputData.Length} bytes");
                return outputData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resampling audio: {ex.Message}");
                return null;
            }
        }
        
        public byte[]? ConvertChannels(byte[] inputData, int inputChannels, int outputChannels, int bitsPerSample)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioFormatConverter));
                
            if (inputData == null || inputData.Length == 0)
                return null;
                
            if (inputChannels == outputChannels)
                return inputData;
                
            try
            {
                var sampleSize = bitsPerSample / 8;
                var sampleCount = inputData.Length / (sampleSize * inputChannels);
                var outputData = new byte[sampleCount * sampleSize * outputChannels];
                
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    if (inputChannels == 1 && outputChannels == 2)
                    {
                        var monoSample = ExtractSample(inputData, sample, 0, inputChannels, sampleSize);
                        WriteSample(outputData, sample, 0, outputChannels, sampleSize, monoSample);
                        WriteSample(outputData, sample, 1, outputChannels, sampleSize, monoSample);
                    }
                    else if (inputChannels == 2 && outputChannels == 1)
                    {
                        var leftSample = ExtractSample(inputData, sample, 0, inputChannels, sampleSize);
                        var rightSample = ExtractSample(inputData, sample, 1, inputChannels, sampleSize);
                        var monoSample = (leftSample + rightSample) / 2;
                        WriteSample(outputData, sample, 0, outputChannels, sampleSize, monoSample);
                    }
                    else
                    {
                        var channelsToProcess = Math.Min(inputChannels, outputChannels);
                        for (int channel = 0; channel < channelsToProcess; channel++)
                        {
                            var channelSample = ExtractSample(inputData, sample, channel, inputChannels, sampleSize);
                            WriteSample(outputData, sample, channel, outputChannels, sampleSize, channelSample);
                        }
                        
                        if (outputChannels > inputChannels)
                        {
                            var firstChannelSample = ExtractSample(inputData, sample, 0, inputChannels, sampleSize);
                            for (int channel = inputChannels; channel < outputChannels; channel++)
                            {
                                WriteSample(outputData, sample, channel, outputChannels, sampleSize, firstChannelSample);
                            }
                        }
                    }
                }
                
                _logger.LogTrace($"Converted channels: {inputChannels} -> {outputChannels}, {inputData.Length} -> {outputData.Length} bytes");
                return outputData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting channels: {ex.Message}");
                return null;
            }
        }
        
        public byte[]? ConvertBitDepth(byte[] inputData, int inputBitsPerSample, int outputBitsPerSample, int channels)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(AudioFormatConverter));
                
            if (inputData == null || inputData.Length == 0)
                return null;
                
            if (inputBitsPerSample == outputBitsPerSample)
                return inputData;
                
            try
            {
                var inputSampleSize = inputBitsPerSample / 8;
                var outputSampleSize = outputBitsPerSample / 8;
                var sampleCount = inputData.Length / (inputSampleSize * channels);
                var outputData = new byte[sampleCount * outputSampleSize * channels];
                
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    for (int channel = 0; channel < channels; channel++)
                    {
                        var inputSample = ExtractSample(inputData, sample, channel, channels, inputSampleSize);
                        var outputSample = ConvertSampleBitDepth(inputSample, inputBitsPerSample, outputBitsPerSample);
                        WriteSample(outputData, sample, channel, channels, outputSampleSize, outputSample);
                    }
                }
                
                _logger.LogTrace($"Converted bit depth: {inputBitsPerSample} -> {outputBitsPerSample} bits, {inputData.Length} -> {outputData.Length} bytes");
                return outputData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting bit depth: {ex.Message}");
                return null;
            }
        }
        
        private byte[]? ConvertAudioData(byte[] inputData, AudioFormat inputFormat, AudioFormat targetFormat)
        {
            var currentData = inputData;
            
            if (inputFormat.SampleFormat != targetFormat.SampleFormat)
            {
                currentData = ConvertSampleFormat(currentData, inputFormat.SampleFormat, targetFormat.SampleFormat);
                if (currentData == null) return null;
            }
            
            if (inputFormat.BitsPerSample != targetFormat.BitsPerSample)
            {
                currentData = ConvertBitDepth(currentData, inputFormat.BitsPerSample, targetFormat.BitsPerSample, inputFormat.Channels);
                if (currentData == null) return null;
            }
            
            if (inputFormat.Channels != targetFormat.Channels)
            {
                currentData = ConvertChannels(currentData, inputFormat.Channels, targetFormat.Channels, targetFormat.BitsPerSample);
                if (currentData == null) return null;
            }
            
            if (inputFormat.SampleRate != targetFormat.SampleRate)
            {
                currentData = ResampleAudio(currentData, inputFormat.SampleRate, targetFormat.SampleRate, targetFormat.Channels, targetFormat.BitsPerSample);
                if (currentData == null) return null;
            }
            
            return currentData;
        }
        
        private byte[]? ConvertSampleFormat(byte[] inputData, AudioSampleFormat inputFormat, AudioSampleFormat outputFormat)
        {
            if (inputFormat == outputFormat)
                return inputData;
                
            try
            {
                return (inputFormat, outputFormat) switch
                {
                    (AudioSampleFormat.ALAW, AudioSampleFormat.PCM) => ConvertAlawToPcm(inputData),
                    (AudioSampleFormat.ULAW, AudioSampleFormat.PCM) => ConvertUlawToPcm(inputData),
                    (AudioSampleFormat.PCM, AudioSampleFormat.ALAW) => ConvertPcmToAlaw(inputData),
                    (AudioSampleFormat.PCM, AudioSampleFormat.ULAW) => ConvertPcmToUlaw(inputData),
                    (AudioSampleFormat.ALAW, AudioSampleFormat.ULAW) => ConvertPcmToUlaw(ConvertAlawToPcm(inputData)),
                    (AudioSampleFormat.ULAW, AudioSampleFormat.ALAW) => ConvertPcmToAlaw(ConvertUlawToPcm(inputData)),
                    _ => inputData // 不支持的转换，返回原数据
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error converting sample format from {inputFormat} to {outputFormat}");
                return null;
            }
        }
        
        private byte[] ConvertAlawToPcm(byte[] alawData)
        {
            var pcmData = new byte[alawData.Length * 2];
            for (int i = 0; i < alawData.Length; i++)
            {
                var pcmSample = AlawToPcm(alawData[i]);
                BitConverter.GetBytes(pcmSample).CopyTo(pcmData, i * 2);
            }
            return pcmData;
        }
        
        private byte[] ConvertUlawToPcm(byte[] ulawData)
        {
            var pcmData = new byte[ulawData.Length * 2];
            for (int i = 0; i < ulawData.Length; i++)
            {
                var pcmSample = UlawToPcm(ulawData[i]);
                BitConverter.GetBytes(pcmSample).CopyTo(pcmData, i * 2);
            }
            return pcmData;
        }
        
        private byte[] ConvertPcmToAlaw(byte[] pcmData)
        {
            var alawData = new byte[pcmData.Length / 2];
            for (int i = 0; i < alawData.Length; i++)
            {
                var pcmSample = BitConverter.ToInt16(pcmData, i * 2);
                alawData[i] = PcmToAlaw(pcmSample);
            }
            return alawData;
        }
        
        private byte[] ConvertPcmToUlaw(byte[] pcmData)
        {
            var ulawData = new byte[pcmData.Length / 2];
            for (int i = 0; i < ulawData.Length; i++)
            {
                var pcmSample = BitConverter.ToInt16(pcmData, i * 2);
                ulawData[i] = PcmToUlaw(pcmSample);
            }
            return ulawData;
        }
        
        private int ExtractSample(byte[] data, int sampleIndex, int channel, int channels, int sampleSize)
        {
            var offset = (sampleIndex * channels + channel) * sampleSize;
            if (offset + sampleSize > data.Length)
                return 0;
                
            return sampleSize switch
            {
                1 => (sbyte)data[offset],
                2 => BitConverter.ToInt16(data, offset),
                4 => BitConverter.ToInt32(data, offset),
                _ => 0
            };
        }
        
        private void WriteSample(byte[] data, int sampleIndex, int channel, int channels, int sampleSize, int sample)
        {
            var offset = (sampleIndex * channels + channel) * sampleSize;
            if (offset + sampleSize > data.Length)
                return;
                
            switch (sampleSize)
            {
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
        
        private int ConvertSampleBitDepth(int sample, int inputBits, int outputBits)
        {
            if (inputBits == outputBits)
                return sample;
                
            if (inputBits < outputBits)
            {
                var shift = outputBits - inputBits;
                return sample << shift;
            }
            else
            {
                var shift = inputBits - outputBits;
                return sample >> shift;
            }
        }
        
        private short AlawToPcm(byte alaw)
        {
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
        
        private short UlawToPcm(byte ulaw)
        {
            ulaw = (byte)~ulaw;
            int sign = ulaw & 0x80;
            int exponent = (ulaw & 0x70) >> 4;
            int mantissa = ulaw & 0x0F;
            
            int sample = ((mantissa << 3) + 0x84) << exponent;
            return (short)(sign != 0 ? -sample : sample);
        }
        
        private byte PcmToAlaw(short pcm)
        {
            int sign = (pcm & 0x8000) >> 8;
            if (sign != 0)
                pcm = (short)-pcm;
                
            if (pcm > 32635)
                pcm = 32635;
                
            int exponent = 7;
            int mantissa;
            
            for (int i = 0x4000; i != 0; i >>= 1, exponent--)
            {
                if ((pcm & i) != 0)
                    break;
            }
            
            mantissa = (pcm >> (exponent > 0 ? exponent + 3 : 4)) & 0x0F;
            return (byte)(sign | (exponent << 4) | mantissa ^ 0x55);
        }
        
        private byte PcmToUlaw(short pcm)
        {
            int sign = (pcm & 0x8000) >> 8;
            if (sign != 0)
                pcm = (short)-pcm;
                
            if (pcm > 32635)
                pcm = 32635;
                
            int exponent = 7;
            int mantissa;
            
            for (int i = 0x4000; i != 0; i >>= 1, exponent--)
            {
                if ((pcm & i) != 0)
                    break;
            }
            
            mantissa = (pcm >> (exponent > 0 ? exponent + 3 : 4)) & 0x0F;
            return (byte)~(sign | (exponent << 4) | mantissa);
        }
        
        public void Dispose()
        {
            if (_disposed)
                return;
                
            _disposed = true;
            _logger.LogInformation("AudioFormatConverter disposed");
        }
    }
}