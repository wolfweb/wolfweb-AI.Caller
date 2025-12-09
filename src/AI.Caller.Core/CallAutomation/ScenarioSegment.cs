namespace AI.Caller.Core.CallAutomation;

/// <summary>
/// 场景片段类型
/// </summary>
public enum ScenarioSegmentType {
    /// <summary>
    /// 录音文件
    /// </summary>
    Recording,

    /// <summary>
    /// TTS文本
    /// </summary>
    TTS,

    /// <summary>
    /// DTMF输入收集
    /// </summary>
    DtmfInput,

    /// <summary>
    /// 条件分支
    /// </summary>
    Condition,

    /// <summary>
    /// 静音
    /// </summary>
    Silence
}

/// <summary>
/// 场景片段
/// </summary>
public class ScenarioSegment {
    /// <summary>
    /// 片段ID
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// 片段顺序
    /// </summary>
    public int Order { get; set; }

    /// <summary>
    /// 片段类型
    /// </summary>
    public ScenarioSegmentType Type { get; set; }

    /// <summary>
    /// 录音文件路径（Type=Recording时使用）
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// TTS文本（Type=TTS时使用）
    /// </summary>
    public string? TtsText { get; set; }

    /// <summary>
    /// TTS变量（格式：{变量名}）
    /// </summary>
    public Dictionary<string, string>? TtsVariables { get; set; }

    /// <summary>
    /// DTMF输入配置（Type=DtmfInput时使用）
    /// </summary>
    public DtmfInputConfig? DtmfConfig { get; set; }

    /// <summary>
    /// 条件表达式（Type=Condition时使用）
    /// </summary>
    public string? ConditionExpression { get; set; }

    /// <summary>
    /// 条件为真时的下一个片段ID
    /// </summary>
    public int? NextSegmentIdOnTrue { get; set; }

    /// <summary>
    /// 条件为假时的下一个片段ID
    /// </summary>
    public int? NextSegmentIdOnFalse { get; set; }

    /// <summary>
    /// 静音时长（毫秒，Type=Silence时使用）
    /// </summary>
    public int? SilenceDurationMs { get; set; }
}

/// <summary>
/// DTMF输入配置
/// </summary>
public class DtmfInputConfig {
    /// <summary>
    /// 模板ID（可选，用于关联数据库记录）
    /// </summary>
    public int? TemplateId { get; set; }

    /// <summary>
    /// 最大长度
    /// </summary>
    public int MaxLength { get; set; }

    /// <summary>
    /// 最小长度
    /// </summary>
    public int MinLength { get; set; }

    /// <summary>
    /// 终止键
    /// </summary>
    public char TerminationKey { get; set; } = '#';

    /// <summary>
    /// 退格键
    /// </summary>
    public char BackspaceKey { get; set; } = '*';

    /// <summary>
    /// 超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// 提示文本
    /// </summary>
    public string? PromptText { get; set; }

    /// <summary>
    /// 成功提示文本
    /// </summary>
    public string? SuccessText { get; set; }

    /// <summary>
    /// 错误提示文本
    /// </summary>
    public string? ErrorText { get; set; }

    /// <summary>
    /// 超时提示文本
    /// </summary>
    public string? TimeoutText { get; set; }

    /// <summary>
    /// 变量名（用于存储输入结果）
    /// </summary>
    public string? VariableName { get; set; }

    /// <summary>
    /// 验证器类型
    /// </summary>
    public string? ValidatorType { get; set; }
}