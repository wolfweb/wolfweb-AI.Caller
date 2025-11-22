namespace AI.Caller.Core {
    public class VadSettings {
        public string ModelFolder        { get; set; } = string.Empty;
        public string ModelFile          { get; set; } = "model.onnx";
        public float  Threshold          { get; set; } = 0.3F;
        public float  MinSilenceDuration { get; set; } = 0.5F;
        public float  MinSpeechDuration  { get; set; } = 0.25F;
        public float  MaxSpeechDuration  { get; set; } = 5.0F;
        public int    WindowSize         { get; set; } = 512;
        public int    Debug              { get; set; } = 0;
    }
}