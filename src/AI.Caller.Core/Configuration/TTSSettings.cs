namespace AI.Caller.Core.Configuration {
    /// <summary>
    /// TTS引擎配置
    /// </summary>
    public class TTSSettings {
        /// <summary>
        /// TTS模型文件夹路径
        /// </summary>
        public string ModelFolder { get; set; } = string.Empty;

        /// <summary>
        /// 模型文件名
        /// </summary>
        public string ModelFile { get; set; } = "model.onnx";

        /// <summary>
        /// 词典文件名
        /// </summary>
        public string LexiconFile { get; set; } = "lexicon.txt";

        /// <summary>
        /// 标记文件名
        /// </summary>
        public string TokensFile { get; set; } = "tokens.txt";

        /// <summary>
        /// 字典目录名
        /// </summary>
        public string DictDir { get; set; } = "dict";

        /// <summary>
        /// 线程数
        /// </summary>
        public int NumThreads { get; set; } = 1;

        /// <summary>
        /// 调试模式
        /// </summary>
        public int Debug { get; set; } = 0;

        /// <summary>
        /// 提供者
        /// </summary>
        public string Provider { get; set; } = "cpu";

        /// <summary>
        /// 规则FST文件列表（逗号分隔）
        /// </summary>
        public string RuleFsts { get; set; } = string.Empty;

        /// <summary>
        /// 是否启用TTS
        /// </summary>
        public bool Enabled { get; set; } = true;
    }
}