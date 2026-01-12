namespace AI.Caller.Core {
    public class RecognizerSettings {
        public string ModelFolder { get; set; } = string.Empty;
        public string ModelFile   { get; set; } = "model.onnx";
        public string Tokens      { get; set; } = "tokens.txt";
        public int    Debug       { get; set; } = 0;
    }
}