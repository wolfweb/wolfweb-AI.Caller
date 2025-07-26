namespace AI.Caller.Phone.Entities;

public class RecordingSetting
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public bool AutoRecording { get; set; } = true;
    public string StoragePath { get; set; } = "recordings";
    public int MaxRetentionDays { get; set; } = 30;
    public long MaxStorageSizeMB { get; set; } = 1024;
    public string AudioFormat { get; set; } = "wav";
    public int AudioQuality { get; set; } = 44100;
    public bool EnableCompression { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    // 导航属性
    public User User { get; set; } = null!;
}