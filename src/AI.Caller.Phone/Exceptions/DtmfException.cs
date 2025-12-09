namespace AI.Caller.Phone.Exceptions;

/// <summary>
/// DTMF相关异常基类
/// </summary>
public class DtmfException : Exception {
    public DtmfException(string message) : base(message) { }
    public DtmfException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// DTMF输入验证失败异常
/// </summary>
public class DtmfValidationException : DtmfException {
    public string Input { get; }
    public string ValidationMessage { get; }

    public DtmfValidationException(string input, string validationMessage)
        : base($"DTMF输入验证失败: {validationMessage}") {
        Input = input;
        ValidationMessage = validationMessage;
    }
}

/// <summary>
/// DTMF输入超时异常
/// </summary>
public class DtmfTimeoutException : DtmfException {
    public int TimeoutSeconds { get; }

    public DtmfTimeoutException(int timeoutSeconds)
        : base($"DTMF输入超时 ({timeoutSeconds}秒)") {
        TimeoutSeconds = timeoutSeconds;
    }
}

/// <summary>
/// DTMF输入重试次数超限异常
/// </summary>
public class DtmfMaxRetriesExceededException : DtmfException {
    public int MaxRetries { get; }

    public DtmfMaxRetriesExceededException(int maxRetries)
        : base($"DTMF输入重试次数超限 (最大{maxRetries}次)") {
        MaxRetries = maxRetries;
    }
}

/// <summary>
/// DTMF模板未找到异常
/// </summary>
public class DtmfTemplateNotFoundException : DtmfException {
    public int TemplateId { get; }

    public DtmfTemplateNotFoundException(int templateId)
        : base($"DTMF输入模板 ID {templateId} 未找到") {
        TemplateId = templateId;
    }
}
