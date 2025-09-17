namespace AI.Caller.Core.Models {
    public class AudioData {
        public byte[]? ByteData { get; set; }
        public float[]? FloatData { get; set; }
        public AudioDataFormat Format { get; set; }
        
        /// <summary>
        /// 音频采样率（Hz）
        /// </summary>
        public int SampleRate { get; set; } = 16000;
        
        /// <summary>
        /// 声道数
        /// </summary>
        public int Channels { get; set; } = 1;
    }

    public enum AudioDataFormat {
        PCM_Byte,
        PCM_Float
    }
}