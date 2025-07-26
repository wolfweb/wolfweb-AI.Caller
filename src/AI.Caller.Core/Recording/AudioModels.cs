namespace AI.Caller.Core.Recording
{
    public class AudioFrame
    {
        public byte[] Data { get; set; }
        
        public AudioFormat Format { get; set; }
        
        public DateTime Timestamp { get; set; }
        
        public AudioSource Source { get; set; }
        
        public uint SequenceNumber { get; set; }
        
        public AudioFrame(byte[] data, AudioFormat format, AudioSource source)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));
            Format = format ?? throw new ArgumentNullException(nameof(format));
            Source = source;
            Timestamp = DateTime.UtcNow;
        }
    }
    
    public class AudioSourceStats
    {
        public int RtpIncomingFrames { get; set; }
        public long RtpIncomingBytes { get; set; }
        public int RtpOutgoingFrames { get; set; }
        public long RtpOutgoingBytes { get; set; }
        public int WebRtcIncomingFrames { get; set; }
        public long WebRtcIncomingBytes { get; set; }
        public int WebRtcOutgoingFrames { get; set; }
        public long WebRtcOutgoingBytes { get; set; }
        public int TotalFrames { get; set; }
        public int BufferSize { get; set; }
        public int MaxBufferSize { get; set; }
        
        public override string ToString()
        {
            return $"RTP In: {RtpIncomingFrames} frames ({RtpIncomingBytes} bytes), " +
                   $"RTP Out: {RtpOutgoingFrames} frames ({RtpOutgoingBytes} bytes), " +
                   $"WebRTC In: {WebRtcIncomingFrames} frames ({WebRtcIncomingBytes} bytes), " +
                   $"WebRTC Out: {WebRtcOutgoingFrames} frames ({WebRtcOutgoingBytes} bytes), " +
                   $"Buffer: {BufferSize}/{MaxBufferSize}";
        }
    }
    
    public class AudioFormat
    {
        public int SampleRate { get; set; }
        
        public int Channels { get; set; }
        
        public int BitsPerSample { get; set; }
        
        public AudioSampleFormat SampleFormat { get; set; }
        
        public int ByteRate => SampleRate * Channels * BitsPerSample / 8;
        
        public int BlockAlign => Channels * BitsPerSample / 8;
        
        public AudioFormat(int sampleRate, int channels, int bitsPerSample, AudioSampleFormat sampleFormat = AudioSampleFormat.PCM)
        {
            SampleRate = sampleRate;
            Channels = channels;
            BitsPerSample = bitsPerSample;
            SampleFormat = sampleFormat;
        }
        
        public bool IsCompatibleWith(AudioFormat other)
        {
            return other != null &&
                   SampleRate == other.SampleRate &&
                   Channels == other.Channels &&
                   BitsPerSample == other.BitsPerSample &&
                   SampleFormat == other.SampleFormat;
        }
        
        public override string ToString()
        {
            return $"{SampleRate}Hz, {Channels}ch, {BitsPerSample}bit, {SampleFormat}";
        }
    }
}