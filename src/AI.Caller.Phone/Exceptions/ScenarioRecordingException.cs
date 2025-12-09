namespace AI.Caller.Phone.Exceptions;

/// <summary>
/// 场景录音相关异常基类
/// </summary>
public class ScenarioRecordingException : Exception {
    public ScenarioRecordingException(string message) : base(message) { }
    public ScenarioRecordingException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 场景录音未找到异常
/// </summary>
public class ScenarioRecordingNotFoundException : ScenarioRecordingException {
    public int ScenarioRecordingId { get; }

    public ScenarioRecordingNotFoundException(int scenarioRecordingId)
        : base($"场景录音 ID {scenarioRecordingId} 未找到") {
        ScenarioRecordingId = scenarioRecordingId;
    }
}

/// <summary>
/// 场景录音片段未找到异常
/// </summary>
public class SegmentNotFoundException : ScenarioRecordingException {
    public int SegmentId { get; }

    public SegmentNotFoundException(int segmentId)
        : base($"场景录音片段 ID {segmentId} 未找到") {
        SegmentId = segmentId;
    }
}

/// <summary>
/// 场景录音文件未找到异常
/// </summary>
public class RecordingFileNotFoundException : ScenarioRecordingException {
    public string FilePath { get; }

    public RecordingFileNotFoundException(string filePath)
        : base($"录音文件未找到: {filePath}") {
        FilePath = filePath;
    }
}

/// <summary>
/// 场景录音播放异常
/// </summary>
public class PlaybackException : ScenarioRecordingException {
    public string CallId { get; }

    public PlaybackException(string callId, string message)
        : base($"通话 {callId} 播放失败: {message}") {
        CallId = callId;
    }

    public PlaybackException(string callId, string message, Exception innerException)
        : base($"通话 {callId} 播放失败: {message}", innerException) {
        CallId = callId;
    }
}
