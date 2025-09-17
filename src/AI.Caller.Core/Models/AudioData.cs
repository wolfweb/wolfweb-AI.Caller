namespace AI.Caller.Core.Models {
    public class AudioData {
        public byte[]? ByteData { get; set; }
        public float[]? FloatData { get; set; }
        public AudioDataFormat Format { get; set; }
    }

    public enum AudioDataFormat {
        PCM_Byte,
        PCM_Float
    }
}