namespace AI.Caller.Core.AI {
    public class OllamaSettings {
        public bool   Enabled        { get; set; } = true;
        public string BaseUrl        { get; set; } = "http://127.0.0.1:11434";
        public string Model          { get; set; } = "";
        public double Temperature    { get; set; } = 0.2;
        public int    MaxTokens      { get; set; } = 512;
        public string ReplyTemplate  { get; set; } = "You are a helpful assistant.\nCONTEXT:\n{context}\n\nUSER: {transcript}\nASSISTANT:";
        public string IntentTemplate { get; set; } = "Classify the user's intent into one of: billing, address, handoff, greet, cancel, unknown.\nReturn only the label.\nUtterance: {transcript}";
    }
}