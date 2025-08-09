using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;
using System.Linq;
using AI.Caller.Core;

namespace AI.Caller.Phone.CallRouting.Services {
    /// <summary>
    /// 通话路由服务实现
    /// </summary>
    public class CallRoutingService : ICallRoutingService {
        private readonly ApplicationContext _applicationContext;
        private readonly AppDbContext _dbContext;
        private readonly ICallTypeIdentifier _callTypeIdentifier;
        private readonly ICallRoutingStrategy _routingStrategy;
        private readonly ILogger<CallRoutingService> _logger;

        public CallRoutingService(
            ApplicationContext applicationContext,
            AppDbContext dbContext,
            ICallTypeIdentifier callTypeIdentifier,
            ICallRoutingStrategy routingStrategy,
            ILogger<CallRoutingService> logger) {
            _applicationContext = applicationContext;
            _dbContext = dbContext;
            _callTypeIdentifier = callTypeIdentifier;
            _routingStrategy = routingStrategy;
            _logger = logger;
        }

        /// <summary>
        /// 路由新呼入通话
        /// </summary>
        public async Task<CallRoutingResult> RouteInboundCallAsync(SIPRequest sipRequest) {
            try {
                _logger.LogInformation($"开始路由新呼入通话 - CallId: {sipRequest.Header.CallId}");

                var toUser = sipRequest.Header.To.ToURI.User;
                if (string.IsNullOrEmpty(toUser)) {
                    return CallRoutingResult.CreateFailure("无法解析被叫号码");
                }

                _logger.LogDebug($"被叫号码: {toUser}");

                var targetUser = await _dbContext.Users.FirstOrDefaultAsync(u => u.SipUsername == toUser);
                SIPClient? sipClient;
                if (targetUser == null) {
                    var client = _applicationContext.SipClients.FirstOrDefault(x => !x.Value.IsCallActive);
                    if(client.Value != null) {
                        sipClient = client.Value;
                        _logger.LogInformation($"未找到用户信息，使用默认客户端 - SipUsername: {client.Key}");
                    } else {
                        _logger.LogWarning($"未找到用户信息且没有可用客户端 - SipUsername: {toUser}");
                        return CallRoutingResult.CreateFailure($"未找到用户: {toUser}", CallHandlingStrategy.Reject);
                    }
                } else {
                    if (!_applicationContext.SipClients.TryGetValue(toUser, out sipClient)) {
                        _logger.LogInformation($"用户客户端不在线 - SipUsername: {toUser}");
                        return CallRoutingResult.CreateFailure($"用户不在线: {toUser}", CallHandlingStrategy.Fallback);
                    }
                }

                if (sipClient.IsCallActive) {
                    _logger.LogInformation($"用户正在通话中 - SipUsername: {toUser}");
                    return CallRoutingResult.CreateFailure($"用户忙碌: {toUser}", CallHandlingStrategy.Fallback);
                }

                var strategy = DetermineCallHandlingStrategy(sipRequest, targetUser, sipClient);
                var result = CallRoutingResult.CreateSuccess(sipClient, targetUser, strategy, $"成功路由到用户: {toUser}");

                _logger.LogInformation($"新呼入路由成功 - SipUsername: {toUser}, Strategy: {strategy}");
                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "路由新呼入通话时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 路由呼出应答
        /// </summary>
        public async Task<CallRoutingResult> RouteOutboundResponseAsync(SIPRequest sipRequest) {
            try {
                var callId = sipRequest.Header.CallId;
                _logger.LogInformation($"开始路由呼出应答 - CallId: {callId}");

                // 1. 根据Call-ID查找对应的呼出记录
                var outboundCallInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);
                if (outboundCallInfo == null) {
                    _logger.LogWarning($"未找到对应的呼出记录 - CallId: {callId}");
                    return CallRoutingResult.CreateFailure("未找到对应的呼出记录");
                }

                // 2. 获取发起呼叫的SipClient
                if (!_applicationContext.SipClients.TryGetValue(outboundCallInfo.SipUsername, out var sipClient)) {
                    _logger.LogWarning($"发起呼叫的客户端不存在 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"发起呼叫的客户端不存在: {outboundCallInfo.SipUsername}");
                }

                // 3. 查找对应用户
                var targetUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.SipUsername == outboundCallInfo.SipUsername);

                // 4. 返回路由结果
                var result = CallRoutingResult.CreateSuccess(
                    sipClient,
                    targetUser,
                    CallHandlingStrategy.WebToNonWeb,
                    $"成功路由呼出应答到: {outboundCallInfo.SipUsername}");

                result.OutboundCallInfo = outboundCallInfo;

                _logger.LogInformation($"呼出应答路由成功 - SipUsername: {outboundCallInfo.SipUsername}");
                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "路由呼出应答时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 确定通话处理策略
        /// </summary>
        private CallHandlingStrategy DetermineCallHandlingStrategy(SIPRequest sipRequest, User targetUser, AI.Caller.Core.SIPClient sipClient) {
            try {
                // 分析来电源
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var isFromWebClient = IsWebClient(fromUser);
                var isToWebClient = IsWebClient(targetUser.SipUsername);

                if (isFromWebClient && isToWebClient) {
                    return CallHandlingStrategy.WebToWeb;
                } else if (isFromWebClient && !isToWebClient) {
                    return CallHandlingStrategy.WebToNonWeb;
                } else if (!isFromWebClient && isToWebClient) {
                    return CallHandlingStrategy.NonWebToWeb;
                } else {
                    return CallHandlingStrategy.NonWebToNonWeb;
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "确定通话处理策略时发生错误");
                return CallHandlingStrategy.NonWebToWeb; // 默认策略
            }
        }

        /// <summary>
        /// 路由外呼请求
        /// </summary>
        public async Task<CallRoutingResult> RouteOutboundCallAsync(OutboundCallInfo outboundCallInfo) {
            try {
                _logger.LogInformation($"开始路由外呼请求 - SipUsername: {outboundCallInfo.SipUsername}, Destination: {outboundCallInfo.Destination}");

                // 1. 验证发起呼叫的用户
                if (!_applicationContext.SipClients.TryGetValue(outboundCallInfo.SipUsername, out var sipClient)) {
                    _logger.LogWarning($"发起呼叫的客户端不存在 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"发起呼叫的客户端不存在: {outboundCallInfo.SipUsername}");
                }

                // 2. 查找发起呼叫的用户
                var callerUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.SipUsername == outboundCallInfo.SipUsername);

                if (callerUser == null) {
                    _logger.LogWarning($"发起呼叫的用户不存在 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"发起呼叫的用户不存在: {outboundCallInfo.SipUsername}");
                }

                // 3. 检查发起呼叫的客户端是否忙碌
                if (sipClient.IsCallActive) {
                    _logger.LogInformation($"发起呼叫的用户正在通话中 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"用户忙碌: {outboundCallInfo.SipUsername}", CallHandlingStrategy.Fallback);
                }

                // 4. 识别呼叫类型 - 创建一个模拟的SIP请求用于类型识别
                // 注意：这里应该传入实际的SIPRequest，但为了简化，我们直接根据目标号码判断类型

                // 5. 确定路由策略
                CallHandlingStrategy strategy;
                if (IsPstnNumber(outboundCallInfo.Destination)) {
                    strategy = CallHandlingStrategy.WebToNonWeb; // Web到PSTN
                } else if (_applicationContext.SipClients.ContainsKey(outboundCallInfo.Destination)) {
                    strategy = CallHandlingStrategy.WebToWeb; // Web到Web
                } else {
                    strategy = CallHandlingStrategy.WebToNonWeb; // 默认为Web到非Web
                }

                // 6. 创建成功的路由结果
                var result = CallRoutingResult.CreateSuccess(
                    sipClient,
                    callerUser,
                    strategy,
                    $"成功路由外呼请求: {outboundCallInfo.SipUsername} -> {outboundCallInfo.Destination}");

                result.OutboundCallInfo = outboundCallInfo;

                _logger.LogInformation($"外呼请求路由成功 - SipUsername: {outboundCallInfo.SipUsername}, Strategy: {strategy}");
                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "路由外呼请求时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 判断是否为PSTN号码
        /// </summary>
        private static bool IsPstnNumber(string number) {
            if (string.IsNullOrEmpty(number))
                return false;

            // 简单的PSTN号码识别逻辑
            // 通常PSTN号码是纯数字，可能包含+号开头
            return number.All(c => char.IsDigit(c) || c == '+') &&
                   number.Length >= 10 &&
                   number.Length <= 15;
        }

        /// <summary>
        /// 判断是否为Web客户端
        /// </summary>
        private bool IsWebClient(string? fromUser) {
            if (string.IsNullOrEmpty(fromUser))
                return false;

            // 简单的启发式判断：检查是否为已知的Web客户端用户
            return _applicationContext.SipClients.ContainsKey(fromUser);
        }
    }
}