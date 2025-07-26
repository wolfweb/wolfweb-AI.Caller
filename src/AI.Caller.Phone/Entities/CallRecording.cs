namespace AI.Caller.Phone.Entities;

public class CallRecording
{
    public int Id { get; set; }
    public string CallId { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string CallerNumber { get; set; } = string.Empty;
    public string CalleeNumber { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime? EndTime { get; set; }
    public TimeSpan Duration { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public string AudioFormat { get; set; } = "wav";
    public RecordingStatus Status { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
    
    // 导航属性
    public User User { get; set; } = null!;
}

public enum RecordingStatus
{
    Recording = 0,
    Completed = 1,
    Failed = 2,
    Deleted = 3
}