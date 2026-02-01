namespace AI.Caller.Core.Media.Interfaces {
    /// <summary>
    /// 统一的音频编解码器接口
    /// 支持G.711和G.722等多种编解码器
    /// </summary>
    public interface IAudioCodec : IDisposable {
        /// <summary>
        /// 编解码器类型
        /// </summary>
        AudioCodec Type { get; }
        
        /// <summary>
        /// 采样率 (Hz)
        /// </summary>
        int SampleRate { get; }
        
        /// <summary>
        /// 声道数
        /// </summary>
        int Channels { get; }
        
        /// <summary>
        /// 编码PCM16数据为特定格式
        /// </summary>
        /// <param name="pcm16">16位PCM数据</param>
        /// <returns>编码后的数据</returns>
        byte[] Encode(ReadOnlySpan<byte> pcm16);
        
        /// <summary>
        /// 解码特定格式数据为PCM16
        /// </summary>
        /// <param name="encoded">编码数据</param>
        /// <returns>16位PCM数据</returns>
        int Decode(ReadOnlySpan<byte> encoded, Span<byte> decodedOutput);
        
        /// <summary>
        /// 生成指定时长的静音帧
        /// </summary>
        /// <param name="durationMs">时长(毫秒)</param>
        /// <returns>编码后的静音帧</returns>
        byte[] GenerateSilenceFrame(int durationMs);
    }
}