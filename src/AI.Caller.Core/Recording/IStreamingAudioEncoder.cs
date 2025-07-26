using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{
    /// <summary>
    /// 流式音频编码器接口，支持实时音频数据写入
    /// </summary>
    public interface IStreamingAudioEncoder : IDisposable
    {
        /// <summary>
        /// 编码器是否已初始化
        /// </summary>
        bool IsInitialized { get; }
        
        /// <summary>
        /// 输出文件路径
        /// </summary>
        string? OutputFilePath { get; }
        
        /// <summary>
        /// 编码选项
        /// </summary>
        AudioEncodingOptions Options { get; }
        
        /// <summary>
        /// 初始化编码器
        /// </summary>
        /// <param name="inputFormat">输入音频格式</param>
        /// <param name="outputPath">输出文件路径</param>
        /// <returns>初始化是否成功</returns>
        Task<bool> InitializeAsync(AudioFormat inputFormat, string outputPath);
        
        /// <summary>
        /// 写入音频帧到文件（实时写入）
        /// </summary>
        /// <param name="frame">音频帧</param>
        /// <returns>写入是否成功</returns>
        Task<bool> WriteAudioFrameAsync(AudioFrame frame);
        
        /// <summary>
        /// 刷新缓冲区到文件
        /// </summary>
        /// <returns>刷新是否成功</returns>
        Task<bool> FlushAsync();
        
        /// <summary>
        /// 完成编码并关闭文件
        /// </summary>
        /// <returns>完成是否成功</returns>
        Task<bool> FinalizeAsync();
        
        /// <summary>
        /// 获取编码器信息
        /// </summary>
        /// <returns>编码器信息</returns>
        EncoderInfo GetEncoderInfo();
        
        /// <summary>
        /// 编码器初始化完成事件
        /// </summary>
        event EventHandler<EncoderInitializedEventArgs>? EncoderInitialized;
        
        /// <summary>
        /// 音频数据写入事件
        /// </summary>
        event EventHandler<AudioWrittenEventArgs>? AudioWritten;
        
        /// <summary>
        /// 编码进度事件
        /// </summary>
        event EventHandler<EncodingProgressEventArgs>? EncodingProgress;
        
        /// <summary>
        /// 编码错误事件
        /// </summary>
        event EventHandler<EncodingErrorEventArgs>? EncodingError;
    }
    
    /// <summary>
    /// 音频写入事件参数
    /// </summary>
    public class AudioWrittenEventArgs : EventArgs
    {
        public long BytesWritten { get; }
        public DateTime WriteTime { get; }
        public bool Success { get; }
        public string? ErrorMessage { get; }
        
        public AudioWrittenEventArgs(long bytesWritten, DateTime writeTime, bool success, string? errorMessage = null)
        {
            BytesWritten = bytesWritten;
            WriteTime = writeTime;
            Success = success;
            ErrorMessage = errorMessage;
        }
    }
}