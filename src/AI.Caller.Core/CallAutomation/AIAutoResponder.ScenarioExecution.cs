using AI.Caller.Core.CallAutomation;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace AI.Caller.Core;

/// <summary>
/// AIAutoResponder - 场景执行功能扩展
/// </summary>
public sealed partial class AIAutoResponder {
    private HashSet<int> _skippedSegmentIds = new();  // 要跳过的片段ID集合

    /// <summary>
    /// 场景执行进度事件
    /// </summary>
    public event Action<ScenarioProgressInfo>? OnScenarioProgress;

    /// <summary>
    /// DTMF输入收集完成事件
    /// </summary>
    public event Action<DtmfInputEventArgs>? OnDtmfInputCollected;

    /// <summary>
    /// 设置要跳过的片段ID列表
    /// </summary>
    public void SetSkippedSegments(List<int> segmentIds) {
        _skippedSegmentIds = new HashSet<int>(segmentIds);
        _logger.LogInformation("已设置跳过片段: {Count} 个", segmentIds.Count);
    }

    /// <summary>
    /// 清空跳过片段列表
    /// </summary>
    public void ClearSkippedSegments() {
        _skippedSegmentIds.Clear();
    }

    /// <summary>
    /// 播放场景录音
    /// </summary>
    /// <param name="segments">场景片段列表</param>
    /// <param name="variables">变量字典</param>
    /// <param name="ct">取消令牌</param>
    public async Task PlayScenarioAsync(
        List<ScenarioSegment> segments,
        Dictionary<string, string> variables,
        CancellationToken ct = default) {
        if (_audioFilePlayer == null) {
            _logger.LogError("AudioFilePlayer未设置，无法播放录音片段");
            throw new InvalidOperationException("AudioFilePlayer未设置");
        }
        
        var hasDtmfSegments = segments.Any(s => s.Type == ScenarioSegmentType.DtmfInput);
        if (hasDtmfSegments) {
            if (_dtmfService == null) {
                _logger.LogError("DtmfService未设置，但场景包含DTMF片段");
                throw new InvalidOperationException("DtmfService未设置，无法处理DTMF输入");
            }
            if (string.IsNullOrEmpty(_currentCallId)) {
                _logger.LogError("CallId未设置，无法处理DTMF输入");
                throw new InvalidOperationException("CallId未设置，无法处理DTMF输入");
            }
        }

        _logger.LogInformation("开始播放场景，共 {SegmentCount} 个片段", segments.Count);
        
        if (_isPaused) {
            _logger.LogWarning("AIAutoResponder处于暂停状态，自动恢复播放");
            await ResumeAsync();
        }

        var orderedSegments = segments.OrderBy(s => s.Order).ToList();
        int currentIndex = 0;
        try {
            while (currentIndex < orderedSegments.Count && !ct.IsCancellationRequested) {
                var segment = orderedSegments[currentIndex];
                
                if (_skippedSegmentIds.Contains(segment.Id)) {
                    _logger.LogInformation("跳过片段 {Order}/{Total}: {Type} (SegmentId={SegmentId})",
                        segment.Order, orderedSegments.Count, segment.Type, segment.Id);
                    currentIndex++;
                    continue;
                }
                
                _logger.LogInformation("播放片段 {Order}/{Total}: {Type} (SegmentId={SegmentId})", segment.Order, orderedSegments.Count, segment.Type, segment.Id);

                try {
                    OnScenarioProgress?.Invoke(new ScenarioProgressInfo {
                        CurrentSegmentIndex = currentIndex,
                        TotalSegments = orderedSegments.Count,
                        CurrentSegment = segment,
                        Status = ScenarioExecutionStatus.ExecutingSegment
                    });

                    switch (segment.Type) {
                        case ScenarioSegmentType.Recording:
                            await PlayRecordingSegmentAsync(segment, ct);
                            currentIndex++;
                            break;

                        case ScenarioSegmentType.TTS:
                            await PlayTtsSegmentAsync(segment, variables, ct);
                            currentIndex++;
                            break;

                        case ScenarioSegmentType.DtmfInput:
                            OnScenarioProgress?.Invoke(new ScenarioProgressInfo {
                                CurrentSegmentIndex = currentIndex,
                                TotalSegments = orderedSegments.Count,
                                CurrentSegment = segment,
                                Status = ScenarioExecutionStatus.WaitingForDtmfInput
                            });
                            
                            await PlayDtmfInputSegmentAsync(segment, variables, ct);
                            currentIndex++;
                            break;

                        case ScenarioSegmentType.Condition:
                            currentIndex = EvaluateConditionSegment(segment, variables, orderedSegments);
                            break;

                        case ScenarioSegmentType.Silence:
                            await PlaySilenceSegmentAsync(segment, ct);
                            currentIndex++;
                            break;

                        default:
                            _logger.LogWarning("未知的片段类型: {Type}，跳过该片段", segment.Type);
                            currentIndex++;
                            break;
                    }
                } catch (OperationCanceledException) {
                    _logger.LogInformation("场景播放被取消");
                    throw;
                } catch (Exception ex) {
                    _logger.LogError(ex, "播放片段失败: {SegmentId}, {Type}，尝试继续执行下一片段", segment.Id, segment.Type);
                    
                    if (segment.Type == ScenarioSegmentType.DtmfInput) {
                        _logger.LogError("DTMF片段执行失败，停止场景播放");
                        throw;
                    } else {
                        _logger.LogWarning("非关键片段失败，继续执行下一片段");
                        currentIndex++;
                    }
                }
            }

            _logger.LogInformation("场景播放完成");
            
            OnScenarioProgress?.Invoke(new ScenarioProgressInfo {
                CurrentSegmentIndex = orderedSegments.Count,
                TotalSegments = orderedSegments.Count,
                CurrentSegment = null,
                Status = ScenarioExecutionStatus.Completed
            });
            
        } catch (Exception ex) {
            _logger.LogError(ex, "场景播放失败");
            
            OnScenarioProgress?.Invoke(new ScenarioProgressInfo {
                CurrentSegmentIndex = currentIndex,
                TotalSegments = orderedSegments.Count,
                CurrentSegment = orderedSegments.ElementAtOrDefault(currentIndex),
                Status = ScenarioExecutionStatus.Failed,
                ErrorMessage = ex.Message
            });
            
            throw;
        }
    }

    /// <summary>
    /// 播放录音片段
    /// </summary>
    private async Task PlayRecordingSegmentAsync(ScenarioSegment segment, CancellationToken ct) {
        if (string.IsNullOrEmpty(segment.FilePath)) {
            _logger.LogWarning("录音片段文件路径为空，SegmentId: {SegmentId}", segment.Id);
            return;
        }

        if (!File.Exists(segment.FilePath)) {
            _logger.LogError("录音文件不存在: {FilePath}", segment.FilePath);
            return;
        }

        var fileInfo = new FileInfo(segment.FilePath);
        
        await PlayRecordingAsync(segment.FilePath, ct);
        await WaitForPlaybackToCompleteAsync();
        
        await Task.Delay(50, ct);
    }

    /// <summary>
    /// 播放TTS片段
    /// </summary>
    private async Task PlayTtsSegmentAsync(
        ScenarioSegment segment,
        Dictionary<string, string> variables,
        CancellationToken ct) {
        if (string.IsNullOrEmpty(segment.TtsText)) {
            _logger.LogWarning("TTS片段文本为空");
            return;
        }

        // 替换变量
        var text = ReplaceVariables(segment.TtsText, variables);
        _logger.LogDebug("TTS文本（替换变量后）: {Text}", text);

        await PlayScriptAsync(text, ct: ct);
        await WaitForPlaybackToCompleteAsync();
    }

    /// <summary>
    /// 播放DTMF输入片段
    /// </summary>
    private async Task PlayDtmfInputSegmentAsync(
        ScenarioSegment segment,
        Dictionary<string, string> variables,
        CancellationToken ct) {
        if (segment.DtmfConfig == null) {
            _logger.LogWarning("DTMF片段配置为空");
            return;
        }

        var config = segment.DtmfConfig;
        int retryCount = 0;
        var startTime = DateTime.UtcNow;

        while (retryCount < config.MaxRetries) {
            try {
                // 播放提示
                if (!string.IsNullOrEmpty(config.PromptText)) {
                    var promptText = ReplaceVariables(config.PromptText, variables);
                    await PlayScriptAsync(promptText, ct: ct);
                    await WaitForPlaybackToCompleteAsync();
                    
                    await Task.Delay(200, ct);
                }

                // 收集输入
                var inputStartTime = DateTime.UtcNow;
                var timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                var input = await CollectDtmfInputAsync(
                    config.MaxLength,
                    config.TerminationKey,
                    config.BackspaceKey,
                    timeout,
                    ct);
                var inputDuration = (int)(DateTime.UtcNow - inputStartTime).TotalMilliseconds;

                // 验证输入
                bool isValid = input.Length >= config.MinLength;
                string? validationMessage = null;

                if (!isValid) {
                    validationMessage = $"输入长度不足: {input.Length} < {config.MinLength}";
                    _logger.LogWarning("DTMF输入长度不足: {Length} < {MinLength}",
                        input.Length, config.MinLength);

                    if (!string.IsNullOrEmpty(config.ErrorText)) {
                        await PlayScriptAsync(config.ErrorText, ct: ct);
                        await WaitForPlaybackToCompleteAsync();
                    }

                    // 保存失败的输入到数据库
                    await SaveDtmfInputToDatabase(segment, config, input, isValid, validationMessage, retryCount, inputDuration);

                    retryCount++;
                    continue;
                }

                // 保存到变量
                if (!string.IsNullOrEmpty(config.VariableName)) {
                    variables[config.VariableName] = input;
                    _logger.LogInformation("DTMF输入已保存到变量: {VariableName}", config.VariableName);
                }

                // 保存成功的输入到数据库
                await SaveDtmfInputToDatabase(segment, config, input, isValid, validationMessage, retryCount, inputDuration);

                // 播放成功提示
                if (!string.IsNullOrEmpty(config.SuccessText)) {
                    await PlayScriptAsync(config.SuccessText, ct: ct);
                    await WaitForPlaybackToCompleteAsync();
                }

                return;
            } catch (TimeoutException) {
                _logger.LogWarning("DTMF输入超时，重试次数: {RetryCount}/{MaxRetries}",
                    retryCount + 1, config.MaxRetries);

                // 保存超时记录到数据库
                var timeoutDuration = (int)(DateTime.UtcNow - startTime).TotalMilliseconds;
                await SaveDtmfInputToDatabase(segment, config, "", false, "输入超时", retryCount, timeoutDuration);

                if (!string.IsNullOrEmpty(config.TimeoutText)) {
                    await PlayScriptAsync(config.TimeoutText, ct: ct);
                    await WaitForPlaybackToCompleteAsync();
                }

                retryCount++;
            }
        }

        throw new Exception($"DTMF输入失败，已达到最大重试次数: {config.MaxRetries}");
    }

    /// <summary>
    /// 保存DTMF输入到数据库
    /// </summary>
    private async Task SaveDtmfInputToDatabase(
        ScenarioSegment segment,
        DtmfInputConfig config,
        string input,
        bool isValid,
        string? validationMessage,
        int retryCount,
        int durationMs) {
        
        _logger.LogInformation("DTMF输入收集完成: CallId={CallId}, Input={MaskedInput}, IsValid={IsValid}, RetryCount={RetryCount}, Duration={Duration}ms",
            _currentCallId, MaskInput(input), isValid, retryCount, durationMs);
        
        // 触发事件，让上层处理数据库保存
        OnDtmfInputCollected?.Invoke(new DtmfInputEventArgs {
            CallId = _currentCallId ?? string.Empty,
            SegmentId = segment.Id,
            TemplateId = config.TemplateId,
            InputValue = input,
            IsValid = isValid,
            ValidationMessage = validationMessage,
            RetryCount = retryCount,
            Duration = durationMs,
            InputTime = DateTime.UtcNow,
            VariableName = config.VariableName
        });
        
        await Task.CompletedTask;
    }

    /// <summary>
    /// 播放静音片段
    /// </summary>
    private async Task PlaySilenceSegmentAsync(ScenarioSegment segment, CancellationToken ct) {
        if (!segment.SilenceDurationMs.HasValue || segment.SilenceDurationMs.Value <= 0) {
            _logger.LogWarning("静音片段时长无效");
            return;
        }

        _logger.LogDebug("播放静音: {Duration}ms", segment.SilenceDurationMs.Value);
        await Task.Delay(segment.SilenceDurationMs.Value, ct);
    }

    /// <summary>
    /// 评估条件片段
    /// </summary>
    private int EvaluateConditionSegment(
        ScenarioSegment segment,
        Dictionary<string, string> variables,
        List<ScenarioSegment> segments) {
        if (string.IsNullOrEmpty(segment.ConditionExpression)) {
            _logger.LogWarning("条件表达式为空");
            return segments.IndexOf(segment) + 1;
        }

        bool result = EvaluateCondition(segment.ConditionExpression, variables);
        _logger.LogInformation("条件评估结果: {Expression} = {Result}",
            segment.ConditionExpression, result);

        if (result && segment.NextSegmentIdOnTrue.HasValue) {
            var nextSegment = segments.FirstOrDefault(s => s.Id == segment.NextSegmentIdOnTrue.Value);
            if (nextSegment != null) {
                return segments.IndexOf(nextSegment);
            }
        } else if (!result && segment.NextSegmentIdOnFalse.HasValue) {
            var nextSegment = segments.FirstOrDefault(s => s.Id == segment.NextSegmentIdOnFalse.Value);
            if (nextSegment != null) {
                return segments.IndexOf(nextSegment);
            }
        }

        // 默认继续下一个片段
        return segments.IndexOf(segment) + 1;
    }

    /// <summary>
    /// 替换文本中的变量
    /// </summary>
    private string ReplaceVariables(string text, Dictionary<string, string> variables) {
        if (string.IsNullOrEmpty(text) || variables == null || variables.Count == 0) {
            return text;
        }

        // 替换 {变量名} 格式的变量
        return Regex.Replace(text, @"\{(\w+)\}", match => {
            var variableName = match.Groups[1].Value;
            if (variables.TryGetValue(variableName, out var value)) {
                return value;
            }
            return match.Value; // 保持原样
        });
    }

    /// <summary>
    /// 评估条件表达式（简单实现）
    /// </summary>
    private bool EvaluateCondition(string expression, Dictionary<string, string> variables) {
        try {
            // 替换变量
            var evaluatedExpression = ReplaceVariables(expression, variables);

            // 简单的条件评估（支持 ==, !=, >, <, >=, <=）
            var operators = new[] { "==", "!=", ">=", "<=", ">", "<" };

            foreach (var op in operators) {
                if (evaluatedExpression.Contains(op)) {
                    var parts = evaluatedExpression.Split(new[] { op }, StringSplitOptions.None);
                    if (parts.Length == 2) {
                        var left = parts[0].Trim().Trim('"', '\'');
                        var right = parts[1].Trim().Trim('"', '\'');

                        return op switch {
                            "==" => left == right,
                            "!=" => left != right,
                            ">" => double.TryParse(left, out var l1) && double.TryParse(right, out var r1) && l1 > r1,
                            "<" => double.TryParse(left, out var l2) && double.TryParse(right, out var r2) && l2 < r2,
                            ">=" => double.TryParse(left, out var l3) && double.TryParse(right, out var r3) && l3 >= r3,
                            "<=" => double.TryParse(left, out var l4) && double.TryParse(right, out var r4) && l4 <= r4,
                            _ => false
                        };
                    }
                }
            }

            // 如果没有运算符，检查变量是否存在且非空
            return !string.IsNullOrEmpty(evaluatedExpression) && evaluatedExpression != "false" && evaluatedExpression != "0";
        } catch (Exception ex) {
            _logger.LogError(ex, "条件表达式评估失败: {Expression}", expression);
            return false;
        }
    }

    /// <summary>
    /// 脱敏输入（用于日志）
    /// </summary>
    private static string MaskInput(string input) {
        if (string.IsNullOrEmpty(input))
            return string.Empty;

        if (input.Length <= 4)
            return new string('*', input.Length);

        // 显示前2位和后2位
        return $"{input.Substring(0, 2)}{"".PadLeft(input.Length - 4, '*')}{input.Substring(input.Length - 2)}";
    }
}

/// <summary>
/// DTMF输入事件参数
/// </summary>
public class DtmfInputEventArgs {
    /// <summary>
    /// 通话ID
    /// </summary>
    public string CallId { get; set; } = string.Empty;

    /// <summary>
    /// 场景片段ID
    /// </summary>
    public int SegmentId { get; set; }

    /// <summary>
    /// 模板ID（可选）
    /// </summary>
    public int? TemplateId { get; set; }

    /// <summary>
    /// 输入值
    /// </summary>
    public string InputValue { get; set; } = string.Empty;

    /// <summary>
    /// 是否有效
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// 验证消息
    /// </summary>
    public string? ValidationMessage { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 输入持续时间（毫秒）
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    /// 输入时间
    /// </summary>
    public DateTime InputTime { get; set; }

    /// <summary>
    /// 变量名（用于存储结果）
    /// </summary>
    public string? VariableName { get; set; }
}
