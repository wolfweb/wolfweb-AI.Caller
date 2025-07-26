namespace AI.Caller.Core.Recording
{
    public class RecordingStatusEventArgs : EventArgs
    {
        public RecordingStatus Status { get; }
        public string? Message { get; }

        public RecordingStatusEventArgs(RecordingStatus status, string? message = null)
        {
            Status = status;
            Message = message;
        }
    }

    public class RecordingProgressEventArgs : EventArgs
    {
        public TimeSpan Duration { get; }
        public long BytesRecorded { get; }
        public double AudioLevel { get; }

        public RecordingProgressEventArgs(TimeSpan duration, long bytesRecorded, double audioLevel = 0.0)
        {
            Duration = duration;
            BytesRecorded = bytesRecorded;
            AudioLevel = audioLevel;
        }
    }

    public class RecordingErrorEventArgs : EventArgs
    {
        public RecordingErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public Exception? Exception { get; }

        public RecordingErrorEventArgs(RecordingErrorCode errorCode, string errorMessage, Exception? exception = null)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            Exception = exception;
        }
    }

    public class AudioDataEventArgs : EventArgs
    {
        public AudioFrame AudioFrame { get; }
        public System.Net.IPEndPoint? RemoteEndPoint { get; }

        public AudioDataEventArgs(AudioFrame audioFrame, System.Net.IPEndPoint? remoteEndPoint = null)
        {
            AudioFrame = audioFrame;
            RemoteEndPoint = remoteEndPoint;
        }
    }

    public class BufferOverflowEventArgs : EventArgs
    {
        public int RemovedFrameCount { get; }
        public int CurrentBufferSize { get; }

        public BufferOverflowEventArgs(int removedFrameCount, int currentBufferSize)
        {
            RemovedFrameCount = removedFrameCount;
            CurrentBufferSize = currentBufferSize;
        }
    }

    public class EncodingProgressEventArgs : EventArgs
    {
        public long BytesEncoded { get; }
        public DateTime Timestamp { get; }

        public EncodingProgressEventArgs(long bytesEncoded, DateTime timestamp)
        {
            BytesEncoded = bytesEncoded;
            Timestamp = timestamp;
        }
    }

    public class EncodingErrorEventArgs : EventArgs
    {
        public RecordingErrorCode ErrorCode { get; }
        public string ErrorMessage { get; }
        public Exception? Exception { get; }

        public EncodingErrorEventArgs(RecordingErrorCode errorCode, string errorMessage, Exception? exception = null)
        {
            ErrorCode = errorCode;
            ErrorMessage = errorMessage;
            Exception = exception;
        }
    }
}