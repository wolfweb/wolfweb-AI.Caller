using Microsoft.Extensions.Logging;

namespace AI.Caller.Core.Recording
{    
    public interface IAudioRecordingManager : IDisposable
    {        
        event EventHandler<RecordingStatusEventArgs>? StatusChanged;
                
        event EventHandler<RecordingProgressEventArgs>? ProgressUpdated;
                
        event EventHandler<RecordingErrorEventArgs>? ErrorOccurred;
                
        RecordingStatus CurrentStatus { get; }
                
        TimeSpan RecordingDuration { get; }
                
        bool IsRecording { get; }
                
        Task<bool> StartRecordingAsync(RecordingOptions options);
                
        Task<string?> StopRecordingAsync();
                
        Task<bool> PauseRecordingAsync();
                
        Task<bool> ResumeRecordingAsync();
                
        Task<bool> CancelRecordingAsync();
                
        void ProcessAudioFrame(AudioFrame audioFrame);
    }
}