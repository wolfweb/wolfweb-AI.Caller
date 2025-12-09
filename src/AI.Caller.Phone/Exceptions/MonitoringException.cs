namespace AI.Caller.Phone.Exceptions;

/// <summary>
/// 监听相关异常基类
/// </summary>
public class MonitoringException : Exception {
    public MonitoringException(string message) : base(message) { }
    public MonitoringException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// 监听会话未找到异常
/// </summary>
public class MonitoringSessionNotFoundException : MonitoringException {
    public int SessionId { get; }

    public MonitoringSessionNotFoundException(int sessionId)
        : base($"监听会话 ID {sessionId} 未找到") {
        SessionId = sessionId;
    }
}

/// <summary>
/// 监听会话已存在异常
/// </summary>
public class MonitoringSessionAlreadyExistsException : MonitoringException {
    public string CallId { get; }

    public MonitoringSessionAlreadyExistsException(string callId)
        : base($"通话 {callId} 已存在活跃的监听会话") {
        CallId = callId;
    }
}

/// <summary>
/// 监听权限不足异常
/// </summary>
public class MonitoringPermissionDeniedException : MonitoringException {
    public int UserId { get; }

    public MonitoringPermissionDeniedException(int userId)
        : base($"用户 ID {userId} 没有监听权限") {
        UserId = userId;
    }
}

/// <summary>
/// 人工接入失败异常
/// </summary>
public class InterventionException : MonitoringException {
    public string CallId { get; }

    public InterventionException(string callId, string message)
        : base($"通话 {callId} 人工接入失败: {message}") {
        CallId = callId;
    }

    public InterventionException(string callId, string message, Exception innerException)
        : base($"通话 {callId} 人工接入失败: {message}", innerException) {
        CallId = callId;
    }
}
