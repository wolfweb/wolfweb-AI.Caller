namespace AI.Caller.Core {
    public class TTSSettings {        
        public string ModelFolder { get; set; } = string.Empty;

        public string ModelFile { get; set; } = "model.onnx";

        public string LexiconFile { get; set; } = "lexicon.txt";

        public string TokensFile { get; set; } = "tokens.txt";

        public string DictDir { get; set; } = "dict";

        public int NumThreads { get; set; } = 1;

        public int Debug { get; set; } = 0;

        public string Provider { get; set; } = "cpu";

        public string RuleFsts { get; set; } = string.Empty;

        public bool Enabled { get; set; } = true;
    }
}