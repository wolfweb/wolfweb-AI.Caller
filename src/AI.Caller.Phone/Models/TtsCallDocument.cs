using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Models {
    /// <summary>
    /// TTS外呼文档记录
    /// </summary>
    public class TtsCallDocument {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string FileName { get; set; } = string.Empty;
        
        [Required]
        public string FilePath { get; set; } = string.Empty;
        
        public DateTime UploadTime { get; set; } = DateTime.UtcNow;
        
        public int TotalRecords { get; set; }
        
        public int CompletedCalls { get; set; }
        
        public int FailedCalls { get; set; }
        
        public TtsCallTaskStatus Status { get; set; } = TtsCallTaskStatus.Pending;
        
        public DateTime? StartTime { get; set; }
        
        public DateTime? EndTime { get; set; }
        
        public int UserId { get; set; }
        
        public virtual ICollection<TtsCallRecord> CallRecords { get; set; } = new List<TtsCallRecord>();
    }
    
    /// <summary>
    /// TTS外呼记录
    /// </summary>
    public class TtsCallRecord {
        public int Id { get; set; }
        
        public int DocumentId { get; set; }
        
        [Required]
        [StringLength(20)]
        public string PhoneNumber { get; set; } = string.Empty;
        
        [StringLength(10)]
        public string Gender { get; set; } = string.Empty;
        
        [StringLength(50)]
        public string AddressTemplate { get; set; } = string.Empty;
        
        [Required]
        public string TtsContent { get; set; } = string.Empty;
        
        public TtsCallStatus CallStatus { get; set; } = TtsCallStatus.Pending;
        
        public DateTime? CallTime { get; set; }
        
        public string? FailureReason { get; set; }
        
        public int RetryCount { get; set; } = 0;
        
        public virtual TtsCallDocument Document { get; set; } = null!;
    }
    
    /// <summary>
    /// 呼入模板
    /// </summary>
    public class InboundTemplate {
        public int Id { get; set; }
        
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [StringLength(500)]
        public string Description { get; set; } = string.Empty;
        
        [Required]
        public string WelcomeScript { get; set; } = string.Empty;
        
        public string? ResponseRules { get; set; }
        
        public bool IsDefault { get; set; } = false;
        
        public bool IsActive { get; set; } = true;
        
        public DateTime CreatedTime { get; set; } = DateTime.UtcNow;
        
        public DateTime? UpdatedTime { get; set; }
        
        public int UserId { get; set; }
    }
    
    public enum TtsCallTaskStatus {
        Pending = 0,
        Running = 1,
        Paused = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }
    
    public enum TtsCallStatus {
        Pending = 0,
        Calling = 1,
        Connected = 2,
        Completed = 3,
        Failed = 4,
        Busy = 5,
        NoAnswer = 6
    }
}