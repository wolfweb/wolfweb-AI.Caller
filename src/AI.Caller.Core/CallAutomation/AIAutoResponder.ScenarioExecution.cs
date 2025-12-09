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
            throw new InvalidOperationException("AudioFilePlayer未设置");
        }

        _logger.LogInformation("开始播放场景，共 {SegmentCount} 个片段", segments.Count);

        try {
            var orderedSegments = segments.OrderBy(s => s.Order).ToList();
            int currentIndex = 0;

            while (currentIndex < orderedSegments.Count && !ct.IsCancellationRequested) {
                var segment = orderedSegments[currentIndex];
                
                // 检查是否应该跳过该片段
                if (_skippedSegmentIds.Contains(segment.Id)) {
                    _logger.LogInformation("跳过片段 {Order}/{Total}: {Type} (SegmentId={SegmentId})",
                        segment.Order, orderedSegments.Count, segment.Type, segment.Id);
                    currentIndex++;
                    continue;
                }
                
                _logger.LogInformation("播放片段 {Order}/{Total}: {Type}",
                    segment.Order, orderedSegments.Count, segment.Type);

                try {
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
                            _logger.LogWarning("未知的片段类型: {Type}", segment.Type);
                            currentIndex++;
                            break;
                    }
                } catch (Exception ex) {
                    _logger.LogError(ex, "播放片段失败: {SegmentId}, {Type}", segment.Id, segment.Type);
                    throw;
                }
            }

            _logger.LogInformation("场景播放完成");
        } catch (Exception ex) {
            _logger.LogError(ex, "场景播放失败");
            throw;
        }
    }

    /// <summary>
    /// 播放录音片段
    /// </summary>
    private async Task PlayRecordingSegmentAsync(ScenarioSegment segment, CancellationToken ct) {
        if (string.IsNullOrEmpty(segment.FilePath)) {
            _logger.LogWarning("录音片段文件路径为空");
            return;
        }

        await PlayRecordingAsync(segment.FilePath, ct);
        await WaitForPlaybackToCompleteAsync();
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
        // 如果没有设置服务或CallId，跳过保存
        if (_dtmfInputService == null || string.IsNullOrEmpty(_currentCallId)) {
            _logger.LogDebug("DtmfInputService或CallId未设置，跳过数据库保存");
            return;
        }

        try {
            // 使用反射调用IDtmfInputService.RecordInputAsync
            // 这样可以避免Core层依赖Phone层
            var serviceType = _dtmfInputService.GetType();
            var recordInputMethod = serviceType.GetMethod("RecordInputAsync");

            if (recordInputMethod == null) {
                _logger.LogWarning("未找到RecordInputAsync方法");
                return;
            }

            // 创建DtmfInputRecord实例
            var recordType = serviceType.Assembly.GetType("AI.Caller.Phone.Entities.DtmfInputRecord");
            if (recordType == null) {
                _logger.LogWarning("未找到DtmfInputRecord类型");
                return;
            }

            var record = Activator.CreateInstance(recordType);
            if (record == null) {
                _logger.LogWarning("无法创建DtmfInputRecord实例");
                return;
            }

            // 设置属性
            recordType.GetProperty("CallId")?.SetValue(record, _currentCallId);
            recordType.GetProperty("SegmentId")?.SetValue(record, segment.Id);
            recordType.GetProperty("TemplateId")?.SetValue(record, config.TemplateId);
            recordType.GetProperty("InputValue")?.SetValue(record, input);
            recordType.GetProperty("IsValid")?.SetValue(record, isValid);
            recordType.GetProperty("ValidationMessage")?.SetValue(record, validationMessage);
            recordType.GetProperty("RetryCount")?.SetValue(record, retryCount);
            recordType.GetProperty("Duration")?.SetValue(record, durationMs);
            recordType.GetProperty("InputTime")?.SetValue(record, DateTime.UtcNow);

            // 调用RecordInputAsync
            var task = recordInputMethod.Invoke(_dtmfInputService, new[] { record }) as Task;
            if (task != null) {
                await task;
                _logger.LogInformation("DTMF输入已保存到数据库: CallId={CallId}, Input={Input}, IsValid={IsValid}",
                    _currentCallId, input, isValid);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "保存DTMF输入到数据库失败");
            // 不影响主流程，继续执行
        }
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
}
