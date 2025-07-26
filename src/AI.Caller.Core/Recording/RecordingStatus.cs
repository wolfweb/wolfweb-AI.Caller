namespace AI.Caller.Core.Recording
{
    
    public class RecordingStatus
    {
        
        public RecordingState State { get; set; }        
        
        public DateTime? StartTime { get; set; }        
        
        public DateTime? EndTime { get; set; }        
        
        public TimeSpan Duration => StartTime.HasValue ? 
            (EndTime ?? DateTime.UtcNow) - StartTime.Value : 
            TimeSpan.Zero;        
        
        public string? CurrentFilePath { get; set; }        
        
        public long BytesRecorded { get; set; }        
        
        public string? ErrorMessage { get; set; }        
        
        public RecordingErrorCode? ErrorCode { get; set; }        
        
        public AudioFormat? AudioFormat { get; set; }        
        
        public RecordingOptions? Options { get; set; }        
        
        public DateTime LastUpdated { get; set; }        
        
        public double AudioLevel { get; set; }        
        
        public bool IsRecording => State == RecordingState.Recording;        
        
        public bool IsPaused => State == RecordingState.Paused;        
        
        public bool IsCompleted => State == RecordingState.Completed;        
        
        public bool HasError => State == RecordingState.Error;        
        
        public bool CanStart => State == RecordingState.Idle || State == RecordingState.Completed || State == RecordingState.Error;
                
        public bool CanStop => State == RecordingState.Recording || State == RecordingState.Paused;        
        
        public bool CanPause => State == RecordingState.Recording;        
        
        public bool CanResume => State == RecordingState.Paused;     
        
        public RecordingStatus()
        {
            State = RecordingState.Idle;
            LastUpdated = DateTime.UtcNow;
        }        
        
        public void UpdateState(RecordingState newState, string? message = null)
        {
            State = newState;
            LastUpdated = DateTime.UtcNow;
            
            if (newState == RecordingState.Recording && !StartTime.HasValue)
            {
                StartTime = DateTime.UtcNow;
            }
            else if ((newState == RecordingState.Completed || newState == RecordingState.Cancelled || newState == RecordingState.Error) && !EndTime.HasValue)
            {
                EndTime = DateTime.UtcNow;
            }
            
            if (!string.IsNullOrEmpty(message))
            {
                ErrorMessage = message;
            }
        }
                
        public void SetError(RecordingErrorCode errorCode, string errorMessage)
        {
            State = RecordingState.Error;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            LastUpdated = DateTime.UtcNow;
            
            if (!EndTime.HasValue)
            {
                EndTime = DateTime.UtcNow;
            }
        }
                
        public void ClearError()
        {
            ErrorCode = null;
            ErrorMessage = null;
            LastUpdated = DateTime.UtcNow;
        }
                
        public RecordingStatus Clone()
        {
            return new RecordingStatus
            {
                State = State,
                StartTime = StartTime,
                EndTime = EndTime,
                CurrentFilePath = CurrentFilePath,
                BytesRecorded = BytesRecorded,
                ErrorMessage = ErrorMessage,
                ErrorCode = ErrorCode,
                AudioFormat = AudioFormat,
                Options = Options,
                LastUpdated = LastUpdated,
                AudioLevel = AudioLevel
            };
        }
        
        public override string ToString()
        {
            return $"State: {State}, Duration: {Duration:hh\\:mm\\:ss}, Bytes: {BytesRecorded:N0}";
        }
    }
}