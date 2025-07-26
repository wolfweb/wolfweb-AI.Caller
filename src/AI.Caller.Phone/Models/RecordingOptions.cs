namespace AI.Caller.Phone.Models;

/// <summary>
/// 录音配置选项
/// </summary>
public class RecordingOptions
{
    public const string SectionName = "RecordingSettings";

    /// <summary>
    /// 存储路径
    /// </summary>
    public string StoragePath { get; set; } = "recordings";

    /// <summary>
    /// 默认音频格式
    /// </summary>
    public string DefaultAudioFormat { get; set; } = "wav";

    /// <summary>
    /// 默认音频质量 (采样率)
    /// </summary>
    public int DefaultAudioQuality { get; set; } = 44100;

    /// <summary>
    /// 默认保留天数
    /// </summary>
    public int DefaultRetentionDays { get; set; } = 30;

    /// <summary>
    /// 默认最大存储大小 (MB)
    /// </summary>
    public long DefaultMaxStorageSizeMB { get; set; } = 1024;

    /// <summary>
    /// 默认是否启用自动录音
    /// </summary>
    public bool DefaultAutoRecording { get; set; } = true;

    /// <summary>
    /// 是否启用自动清理
    /// </summary>
    public bool EnableAutoCleanup { get; set; } = true;

    /// <summary>
    /// 自动清理间隔 (小时)
    /// </summary>
    public int AutoCleanupIntervalHours { get; set; } = 24;

    /// <summary>
    /// 临时文件清理间隔 (小时)
    /// </summary>
    public int TempFileCleanupIntervalHours { get; set; } = 1;

    /// <summary>
    /// 最大文件大小 (MB)
    /// </summary>
    public long MaxFileSizeMB { get; set; } = 100;

    /// <summary>
    /// 是否启用压缩
    /// </summary>
    public bool EnableCompression { get; set; } = true;
}