using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;

namespace AI.Caller.Phone.CallRouting.Services
{
    /// <summary>
    /// 来电类型识别器实现
    /// </summary>
    public class CallTypeIdentifier : ICallTypeIdentifier
    {
        private readonly ConcurrentDictionary<string, OutboundCallInfo> _outboundCalls;
        private readonly ConcurrentDictionary<string, OutboundCallInfo> _outboundCallsByDialogue;
        private readonly ILogger<CallTypeIdentifier> _logger;
        private readonly Timer _cleanupTimer;

        public CallTypeIdentifier(ILogger<CallTypeIdentifier> logger)
        {
            _logger = logger;
            _outboundCalls = new ConcurrentDictionary<string, OutboundCallInfo>();
            _outboundCallsByDialogue = new ConcurrentDictionary<string, OutboundCallInfo>();
            
            // 每5分钟清理一次过期的呼出记录
            _cleanupTimer = new Timer(CleanupExpiredCalls, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        }

        /// <summary>
        /// 识别来电类型
        /// </summary>
        public CallType IdentifyCallType(SIPRequest sipRequest)
        {
            try
            {
                var callId = sipRequest.Header.CallId;
                var fromTag = sipRequest.Header.From?.FromTag;
                var toTag = sipRequest.Header.To?.ToTag;
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var toUser = sipRequest.Header.To?.ToURI?.User;

                _logger.LogDebug($"开始识别来电类型 - CallId: {callId}, FromTag: {fromTag}, ToTag: {toTag}, From: {fromUser}, To: {toUser}, Method: {sipRequest.Method}");

                if (string.IsNullOrEmpty(callId))
                {
                    _logger.LogWarning("SIP请求缺少Call-ID，默认为新呼入");
                    return CallType.InboundCall;
                }

                // 1. 使用SIP对话标识符进行精确匹配（最可靠的方法）
                var dialogueMatch = FindOutboundCallByDialogue(callId, fromTag, toTag);
                if (dialogueMatch != null)
                {
                    if (IsValidOutboundResponse(sipRequest, dialogueMatch))
                    {
                        _logger.LogInformation($"通过SIP对话标识符匹配识别为呼出应答 - CallId: {callId}, FromTag: {fromTag}, ToTag: {toTag}, SipUsername: {dialogueMatch.SipUsername}");
                        
                        // 如果是首次收到To-tag，更新记录
                        if (string.IsNullOrEmpty(dialogueMatch.ToTag) && !string.IsNullOrEmpty(toTag))
                        {
                            dialogueMatch.ToTag = toTag;
                            // 更新对话索引
                            var newDialogueId = dialogueMatch.GetDialogueId();
                            _outboundCallsByDialogue.TryAdd(newDialogueId, dialogueMatch);
                            _logger.LogDebug($"更新对话标识符 - 新DialogueId: {newDialogueId}");
                        }
                        
                        UpdateOutboundCallStatus(dialogueMatch, sipRequest);
                        return CallType.OutboundResponse;
                    }
                }

                // 2. 尝试通过Call-ID匹配（备用方法）
                if (_outboundCalls.TryGetValue(callId, out var callIdMatch))
                {
                    if (IsValidOutboundResponse(sipRequest, callIdMatch))
                    {
                        _logger.LogInformation($"通过Call-ID匹配识别为呼出应答 - CallId: {callId}, SipUsername: {callIdMatch.SipUsername}");
                        
                        // 更新标签信息
                        if (string.IsNullOrEmpty(callIdMatch.ToTag) && !string.IsNullOrEmpty(toTag))
                        {
                            callIdMatch.ToTag = toTag;
                            var dialogueId = callIdMatch.GetDialogueId();
                            _outboundCallsByDialogue.TryAdd(dialogueId, callIdMatch);
                        }
                        
                        UpdateOutboundCallStatus(callIdMatch, sipRequest);
                        return CallType.OutboundResponse;
                    }
                }

                // 3. 默认为新呼入
                var additionalInfo = AnalyzeSipHeaders(sipRequest);
                _logger.LogInformation($"识别为新呼入 - CallId: {callId}, FromTag: {fromTag}, ToTag: {toTag}, From: {fromUser}, To: {toUser}, Info: {additionalInfo}");
                
                return CallType.InboundCall;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "识别来电类型时发生错误");
                return CallType.InboundCall; // 默认为新呼入
            }
        }

        /// <summary>
        /// 通过SIP对话标识符查找呼出通话
        /// </summary>
        private OutboundCallInfo? FindOutboundCallByDialogue(string callId, string? fromTag, string? toTag)
        {
            try
            {
                // 查找所有可能匹配的呼出通话
                var candidates = _outboundCalls.Values
                    .Where(call => call.CallId.Equals(callId, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var candidate in candidates)
                {
                    if (candidate.MatchesDialogue(callId, fromTag, toTag))
                    {
                        return candidate;
                    }
                }

                // 如果没有找到精确匹配，尝试通过对话ID查找
                if (!string.IsNullOrEmpty(fromTag))
                {
                    var dialogueId = string.IsNullOrEmpty(toTag) ? $"{callId}:{fromTag}" : $"{callId}:{fromTag}:{toTag}";
                    if (_outboundCallsByDialogue.TryGetValue(dialogueId, out var dialogueMatch))
                    {
                        return dialogueMatch;
                    }

                    // 尝试不带To-tag的对话ID
                    if (!string.IsNullOrEmpty(toTag))
                    {
                        var partialDialogueId = $"{callId}:{fromTag}";
                        if (_outboundCallsByDialogue.TryGetValue(partialDialogueId, out var partialMatch))
                        {
                            return partialMatch;
                        }
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "通过SIP对话标识符查找呼出通话时发生错误");
                return null;
            }
        }

        /// <summary>
        /// 执行启发式分析
        /// </summary>
        private CallType PerformHeuristicAnalysis(SIPRequest sipRequest)
        {
            try
            {
                // 检查是否有特定的SIP头部标识表明这是一个应答
                var userAgent = sipRequest.Header.UserAgent;
                var contact = sipRequest.Header.Contact?.FirstOrDefault()?.ContactURI?.ToString();

                // 如果User-Agent或Contact包含已知的运营商标识，且有最近的呼出记录
                if (!string.IsNullOrEmpty(userAgent) || !string.IsNullOrEmpty(contact))
                {
                    var recentOutboundCalls = _outboundCalls.Values
                        .Where(call => DateTime.UtcNow - call.CreatedAt < TimeSpan.FromMinutes(2))
                        .Count();

                    if (recentOutboundCalls > 0)
                    {
                        _logger.LogDebug($"检测到最近有 {recentOutboundCalls} 个呼出记录，可能是应答");
                        // 这里可以添加更多的启发式规则
                    }
                }

                return CallType.InboundCall; // 默认
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "执行启发式分析时发生错误");
                return CallType.InboundCall;
            }
        }

        /// <summary>
        /// 验证是否为有效的呼出应答
        /// </summary>
        private bool IsValidOutboundResponse(SIPRequest sipRequest, OutboundCallInfo outboundCall)
        {
            try
            {
                // 1. 检查时间窗口（呼出记录不应该太旧）
                var timeSinceCreated = DateTime.UtcNow - outboundCall.CreatedAt;
                if (timeSinceCreated > TimeSpan.FromMinutes(5))
                {
                    _logger.LogWarning($"呼出记录过旧: {timeSinceCreated.TotalMinutes} 分钟");
                    return false;
                }

                // 2. 检查方法类型（通常应答会是INVITE的响应或其他相关方法）
                if (sipRequest.Method != SIPMethodsEnum.INVITE && 
                    sipRequest.Method != SIPMethodsEnum.BYE &&
                    sipRequest.Method != SIPMethodsEnum.CANCEL &&
                    sipRequest.Method != SIPMethodsEnum.ACK)
                {
                    _logger.LogDebug($"非标准应答方法: {sipRequest.Method}");
                }

                // 3. 增强的号码匹配检查 - 考虑运营商号码转换
                var matchScore = CalculateNumberMatchScore(sipRequest, outboundCall);
                _logger.LogDebug($"号码匹配得分: {matchScore} - CallId: {outboundCall.CallId}");

                // 如果匹配得分太低，可能不是真正的呼出应答
                if (matchScore < 0.3) // 30%的匹配阈值
                {
                    _logger.LogWarning($"号码匹配得分过低: {matchScore} - 可能不是呼出应答");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "验证呼出应答时发生错误");
                return false;
            }
        }

        /// <summary>
        /// 计算号码匹配得分（考虑运营商转换）
        /// </summary>
        private double CalculateNumberMatchScore(SIPRequest sipRequest, OutboundCallInfo outboundCall)
        {
            try
            {
                var score = 0.0;
                var factors = 0;

                // 获取SIP请求中的号码信息
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var toUser = sipRequest.Header.To?.ToURI?.User;
                var originalDestination = outboundCall.Destination;

                _logger.LogDebug($"号码匹配分析 - From: {fromUser}, To: {toUser}, Original: {originalDestination}");

                // 因子1: From号码与原始目标号码的匹配
                if (!string.IsNullOrEmpty(fromUser) && !string.IsNullOrEmpty(originalDestination))
                {
                    var fromMatchScore = CalculatePhoneNumberSimilarity(fromUser, originalDestination);
                    score += fromMatchScore * 0.6; // 60%权重
                    factors++;
                    _logger.LogDebug($"From号码匹配得分: {fromMatchScore}");
                }

                // 因子2: To号码与发起用户的匹配
                if (!string.IsNullOrEmpty(toUser) && !string.IsNullOrEmpty(outboundCall.SipUsername))
                {
                    var toMatchScore = CalculatePhoneNumberSimilarity(toUser, outboundCall.SipUsername);
                    score += toMatchScore * 0.4; // 40%权重
                    factors++;
                    _logger.LogDebug($"To号码匹配得分: {toMatchScore}");
                }

                // 因子3: 检查是否有相同的域名或主机
                var domainMatchScore = CalculateDomainMatchScore(sipRequest, outboundCall);
                if (domainMatchScore > 0)
                {
                    score += domainMatchScore * 0.2; // 20%权重
                    factors++;
                    _logger.LogDebug($"域名匹配得分: {domainMatchScore}");
                }

                // 计算平均得分
                var finalScore = factors > 0 ? score / factors : 0.0;
                return Math.Min(1.0, finalScore); // 确保不超过1.0
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算号码匹配得分时发生错误");
                return 0.0;
            }
        }

        /// <summary>
        /// 计算电话号码相似度
        /// </summary>
        private double CalculatePhoneNumberSimilarity(string number1, string number2)
        {
            if (string.IsNullOrEmpty(number1) || string.IsNullOrEmpty(number2))
                return 0.0;

            // 标准化号码（移除非数字字符）
            var normalized1 = NormalizePhoneNumber(number1);
            var normalized2 = NormalizePhoneNumber(number2);

            if (string.IsNullOrEmpty(normalized1) || string.IsNullOrEmpty(normalized2))
                return 0.0;

            // 完全匹配
            if (normalized1 == normalized2)
                return 1.0;

            // 后缀匹配（考虑国家代码差异）
            var suffixMatch = CalculateSuffixMatch(normalized1, normalized2);
            if (suffixMatch > 0.8)
                return suffixMatch;

            // 编辑距离匹配
            var editDistance = CalculateEditDistance(normalized1, normalized2);
            var maxLength = Math.Max(normalized1.Length, normalized2.Length);
            var similarity = 1.0 - (double)editDistance / maxLength;

            return Math.Max(0.0, similarity);
        }

        /// <summary>
        /// 标准化电话号码
        /// </summary>
        private string NormalizePhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            // 移除所有非数字字符
            var normalized = new string(phoneNumber.Where(char.IsDigit).ToArray());
            
            // 移除可能的前导零
            normalized = normalized.TrimStart('0');
            
            return normalized;
        }

        /// <summary>
        /// 计算后缀匹配度
        /// </summary>
        private double CalculateSuffixMatch(string number1, string number2)
        {
            var minLength = Math.Min(number1.Length, number2.Length);
            if (minLength < 4) // 至少4位数字才有意义
                return 0.0;

            // 从后往前比较
            var matchCount = 0;
            for (int i = 1; i <= minLength; i++)
            {
                if (number1[number1.Length - i] == number2[number2.Length - i])
                {
                    matchCount++;
                }
                else
                {
                    break;
                }
            }

            // 如果后7位或更多位匹配，认为是同一号码
            if (matchCount >= 7)
                return 1.0;
            
            // 如果后4-6位匹配，给予较高分数
            if (matchCount >= 4)
                return 0.8 + (matchCount - 4) * 0.05;

            return (double)matchCount / minLength;
        }

        /// <summary>
        /// 计算编辑距离
        /// </summary>
        private int CalculateEditDistance(string s1, string s2)
        {
            var len1 = s1.Length;
            var len2 = s2.Length;
            var dp = new int[len1 + 1, len2 + 1];

            for (int i = 0; i <= len1; i++)
                dp[i, 0] = i;
            for (int j = 0; j <= len2; j++)
                dp[0, j] = j;

            for (int i = 1; i <= len1; i++)
            {
                for (int j = 1; j <= len2; j++)
                {
                    var cost = s1[i - 1] == s2[j - 1] ? 0 : 1;
                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost
                    );
                }
            }

            return dp[len1, len2];
        }

        /// <summary>
        /// 计算域名匹配得分
        /// </summary>
        private double CalculateDomainMatchScore(SIPRequest sipRequest, OutboundCallInfo outboundCall)
        {
            try
            {
                var fromHost = sipRequest.Header.From?.FromURI?.Host;
                var toHost = sipRequest.Header.To?.ToURI?.Host;
                
                // 如果域名相同，给予额外分数
                if (!string.IsNullOrEmpty(fromHost) && !string.IsNullOrEmpty(toHost))
                {
                    if (fromHost.Equals(toHost, StringComparison.OrdinalIgnoreCase))
                        return 0.5;
                }

                return 0.0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "计算域名匹配得分时发生错误");
                return 0.0;
            }
        }

        /// <summary>
        /// 更新呼出通话状态
        /// </summary>
        private void UpdateOutboundCallStatus(OutboundCallInfo outboundCall, SIPRequest sipRequest)
        {
            try
            {
                switch (sipRequest.Method)
                {
                    case SIPMethodsEnum.INVITE:
                        // 根据内容判断是初始INVITE还是re-INVITE
                        if (sipRequest.Header.ContentLength > 0)
                        {
                            outboundCall.Status = CallStatus.Answered;
                            outboundCall.AnsweredAt = DateTime.UtcNow;
                        }
                        else
                        {
                            outboundCall.Status = CallStatus.Ringing;
                        }
                        break;
                    case SIPMethodsEnum.ACK:
                        outboundCall.Status = CallStatus.Answered;
                        if (!outboundCall.AnsweredAt.HasValue)
                        {
                            outboundCall.AnsweredAt = DateTime.UtcNow;
                        }
                        break;
                    case SIPMethodsEnum.BYE:
                        outboundCall.Status = CallStatus.Ended;
                        outboundCall.EndedAt = DateTime.UtcNow;
                        break;
                    case SIPMethodsEnum.CANCEL:
                        outboundCall.Status = CallStatus.Failed;
                        outboundCall.EndedAt = DateTime.UtcNow;
                        break;
                    default:
                        // 保持当前状态
                        break;
                }

                _logger.LogDebug($"更新呼出通话状态 - CallId: {outboundCall.CallId}, Status: {outboundCall.Status}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "更新呼出通话状态时发生错误");
            }
        }

        /// <summary>
        /// 分析SIP头部信息
        /// </summary>
        private string AnalyzeSipHeaders(SIPRequest sipRequest)
        {
            try
            {
                var analysis = new List<string>();

                // 分析User-Agent
                if (!string.IsNullOrEmpty(sipRequest.Header.UserAgent))
                {
                    analysis.Add($"UA:{sipRequest.Header.UserAgent}");
                }

                // 分析Contact头部
                if (sipRequest.Header.Contact?.Count > 0)
                {
                    var contact = sipRequest.Header.Contact[0];
                    analysis.Add($"Contact:{contact.ContactURI?.Host}");
                }

                // 分析Via头部
                if (sipRequest.Header.Vias != null)
                {
                    var via = sipRequest.Header.Vias.TopViaHeader;
                    if (via != null)
                    {
                        analysis.Add($"Via:{via.Host}:{via.Port}");
                    }
                }

                return string.Join(", ", analysis);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分析SIP头部时发生错误");
                return "分析失败";
            }
        }

        /// <summary>
        /// 注册呼出通话
        /// </summary>
        public void RegisterOutboundCall(string callId, string sipUsername, string destination)
        {
            // 生成From-tag（如果没有提供）
            var fromTag = GenerateFromTag();
            RegisterOutboundCallWithSipTags(callId, fromTag, sipUsername, destination);
        }

        /// <summary>
        /// 注册呼出通话（使用SIP标签）
        /// </summary>
        public void RegisterOutboundCallWithSipTags(string callId, string fromTag, string sipUsername, string destination)
        {
            try
            {
                var normalizedDestination = NormalizePhoneNumber(destination);
                
                var outboundCallInfo = new OutboundCallInfo
                {
                    CallId = callId,
                    FromTag = fromTag,
                    SipUsername = sipUsername,
                    Destination = destination,
                    NormalizedDestination = normalizedDestination,
                    CreatedAt = DateTime.UtcNow,
                    Status = CallStatus.Initiated,
                    ClientId = $"outbound_{sipUsername}_{DateTime.UtcNow.Ticks}"
                };

                // 同时使用Call-ID和对话ID作为索引
                _outboundCalls.TryAdd(callId, outboundCallInfo);
                var dialogueId = outboundCallInfo.GetDialogueId();
                _outboundCallsByDialogue.TryAdd(dialogueId, outboundCallInfo);

                _logger.LogInformation($"注册呼出通话 - CallId: {callId}, FromTag: {fromTag}, SipUsername: {sipUsername}, Destination: {destination}, DialogueId: {dialogueId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"注册呼出通话失败 - CallId: {callId}");
            }
        }

        /// <summary>
        /// 生成From标签
        /// </summary>
        private string GenerateFromTag()
        {
            // 生成符合SIP标准的From-tag
            return $"tag-{Guid.NewGuid():N}";
        }

        /// <summary>
        /// 注册呼出通话（增强版本，包含更多上下文信息）
        /// </summary>
        public void RegisterOutboundCallWithContext(string callId, string sipUsername, string destination, string? originalFromUri = null, string? originalToUri = null)
        {
            try
            {
                var outboundCallInfo = new OutboundCallInfo
                {
                    CallId = callId,
                    SipUsername = sipUsername,
                    Destination = destination,
                    CreatedAt = DateTime.UtcNow,
                    Status = CallStatus.Initiated,
                    ClientId = $"outbound_{sipUsername}_{DateTime.UtcNow.Ticks}"
                };

                _outboundCalls.TryAdd(callId, outboundCallInfo);
                _logger.LogInformation($"注册呼出通话(增强) - CallId: {callId}, SipUsername: {sipUsername}, Destination: {destination}, From: {originalFromUri}, To: {originalToUri}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"注册呼出通话(增强)失败 - CallId: {callId}");
            }
        }

        /// <summary>
        /// 根据多个条件查找呼出通话（用于处理号码转换情况）
        /// </summary>
        public OutboundCallInfo? FindOutboundCallByContext(SIPRequest sipRequest)
        {
            try
            {
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var toUser = sipRequest.Header.To?.ToURI?.User;
                var callId = sipRequest.Header.CallId;

                // 首先尝试精确的CallId匹配
                if (!string.IsNullOrEmpty(callId) && _outboundCalls.TryGetValue(callId, out var exactMatch))
                {
                    return exactMatch;
                }

                // 如果CallId不匹配，尝试基于号码和时间的模糊匹配
                var recentCalls = _outboundCalls.Values
                    .Where(call => DateTime.UtcNow - call.CreatedAt < TimeSpan.FromMinutes(5))
                    .ToList();

                OutboundCallInfo? bestMatch = null;
                double bestScore = 0.0;

                foreach (var call in recentCalls)
                {
                    var score = CalculateNumberMatchScore(sipRequest, call);
                    if (score > bestScore && score > 0.5) // 至少50%匹配度
                    {
                        bestScore = score;
                        bestMatch = call;
                    }
                }

                if (bestMatch != null)
                {
                    _logger.LogInformation($"通过模糊匹配找到呼出通话 - 匹配度: {bestScore:F2}, CallId: {bestMatch.CallId}");
                }

                return bestMatch;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "根据上下文查找呼出通话时发生错误");
                return null;
            }
        }

        /// <summary>
        /// 注销呼出通话
        /// </summary>
        public void UnregisterOutboundCall(string callId)
        {
            try
            {
                if (_outboundCalls.TryRemove(callId, out var removedCall))
                {
                    _logger.LogInformation($"注销呼出通话 - CallId: {callId}, SipUsername: {removedCall.SipUsername}");
                }
                else
                {
                    _logger.LogWarning($"尝试注销不存在的呼出通话 - CallId: {callId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"注销呼出通话失败 - CallId: {callId}");
            }
        }

        /// <summary>
        /// 获取呼出通话信息
        /// </summary>
        public OutboundCallInfo? GetOutboundCallInfo(string callId)
        {
            _outboundCalls.TryGetValue(callId, out var callInfo);
            return callInfo;
        }

        /// <summary>
        /// 更新呼出通话状态（外部调用）
        /// </summary>
        public void UpdateOutboundCallStatus(string callId, CallStatus status)
        {
            try
            {
                if (_outboundCalls.TryGetValue(callId, out var outboundCall))
                {
                    outboundCall.Status = status;
                    
                    if (status == CallStatus.Answered && !outboundCall.AnsweredAt.HasValue)
                    {
                        outboundCall.AnsweredAt = DateTime.UtcNow;
                    }
                    else if (status == CallStatus.Ended && !outboundCall.EndedAt.HasValue)
                    {
                        outboundCall.EndedAt = DateTime.UtcNow;
                    }

                    _logger.LogInformation($"外部更新呼出通话状态 - CallId: {callId}, Status: {status}");
                }
                else
                {
                    _logger.LogWarning($"尝试更新不存在的呼出通话状态 - CallId: {callId}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"更新呼出通话状态失败 - CallId: {callId}");
            }
        }

        /// <summary>
        /// 获取所有活跃的呼出通话
        /// </summary>
        public IEnumerable<OutboundCallInfo> GetActiveOutboundCalls()
        {
            return _outboundCalls.Values
                .Where(call => call.Status != CallStatus.Ended && call.Status != CallStatus.Failed)
                .ToList();
        }

        /// <summary>
        /// 获取呼出通话统计信息
        /// </summary>
        public (int Total, int Active, int Answered, int Failed) GetOutboundCallStats()
        {
            var calls = _outboundCalls.Values.ToList();
            return (
                Total: calls.Count,
                Active: calls.Count(c => c.Status == CallStatus.Initiated || c.Status == CallStatus.Ringing),
                Answered: calls.Count(c => c.Status == CallStatus.Answered),
                Failed: calls.Count(c => c.Status == CallStatus.Failed)
            );
        }

        /// <summary>
        /// 清理过期的呼出通话记录
        /// </summary>
        private void CleanupExpiredCalls(object? state)
        {
            try
            {
                var expiredTime = DateTime.UtcNow.AddMinutes(-10); // 10分钟前的记录视为过期
                var expiredCalls = _outboundCalls
                    .Where(kvp => kvp.Value.CreatedAt < expiredTime)
                    .ToList();

                foreach (var kvp in expiredCalls)
                {
                    var callId = kvp.Key;
                    var callInfo = kvp.Value;
                    
                    // 从Call-ID索引中移除
                    if (_outboundCalls.TryRemove(callId, out var removedCall))
                    {
                        // 从对话ID索引中移除
                        var dialogueId = removedCall.GetDialogueId();
                        _outboundCallsByDialogue.TryRemove(dialogueId, out _);
                        
                        _logger.LogDebug($"清理过期呼出记录 - CallId: {callId}, DialogueId: {dialogueId}, SipUsername: {removedCall.SipUsername}");
                    }
                }

                if (expiredCalls.Count > 0)
                {
                    _logger.LogInformation($"清理了 {expiredCalls.Count} 个过期的呼出通话记录");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "清理过期呼出通话记录时发生错误");
            }
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            _cleanupTimer?.Dispose();
        }
    }
}