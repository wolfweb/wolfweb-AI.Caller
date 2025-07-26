namespace AI.Caller.Core.Recording
{
    public enum AudioCodec
    {
        PCM_WAV,

        MP3,

        AAC,

        OPUS
    }


    public enum AudioSource
    {
        RTP_Incoming,

        RTP_Outgoing,

        WebRTC_Incoming,

        WebRTC_Outgoing,

        Mixed
    }


    public enum AudioSampleFormat
    {
        PCM,

        ALAW,

        ULAW,

        Float
    }


    public enum RecordingState
    {
        Idle,

        Starting,

        Recording,

        Paused,

        Stopping,

        Completed,

        Cancelled,

        Error
    }


    public enum AudioQuality
    {

        Low,

        Standard,

        High,

        Lossless
    }


    public enum RecordingErrorCode
    {

        InitializationFailed,

        EncodingFailed,

        StorageFailed,

        InvalidFormat,

        InsufficientSpace,

        PermissionDenied,

        NetworkError,

        Timeout,

        ConfigurationError,

        Unknown
    }
}