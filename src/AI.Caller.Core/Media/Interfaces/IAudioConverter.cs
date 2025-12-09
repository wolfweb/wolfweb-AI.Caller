namespace AI.Caller.Core.Media.Interfaces;

/// <summary>
/// 音频格式转换服务接口
/// </summary>
public interface IAudioConverter : IDisposable {
    /// <summary>
    /// 将音频文件转换为 PCM 格式
    /// </summary>
    /// <param name="inputPath">输入文件路径</param>
    /// <param name="outputPath">输出文件路径</param>
    /// <param name="sampleRate">目标采样率（默认8000Hz）</param>
    /// <param name="channels">目标声道数（默认1=单声道）</param>
    /// <returns>转换是否成功</returns>
    Task<bool> ConvertToPcmAsync(string inputPath, string outputPath, int sampleRate = 8000, int channels = 1);
}
