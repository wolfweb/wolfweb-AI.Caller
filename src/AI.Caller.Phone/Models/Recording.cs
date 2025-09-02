namespace AI.Caller.Phone.Models {
    public class Recording {
        public int Id { get; set; }

        public int UserId { get; set; }

        public string SipUsername { get; set; } = string.Empty;

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public string FilePath { get; set; } = string.Empty;

        public long FileSize { get; set; }

        public TimeSpan Duration { get; set; }

        public RecordingStatus Status { get; set; }
    }

    public enum RecordingStatus {
        Recording = 0,
        Completed = 1,
        Failed = 2
    }
}