using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.CallRouting.Services
{
    /// <summary>
    /// 通话路由服务实现
    /// </summary>
    public class CallRoutingService : ICallRoutingService
    {
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
            ILogger<CallRoutingService> logger)
        {
            _applicationContext = applicationContext;
            _dbContext = dbContext;
            _callTypeIdentifier = callTypeIdentifier;
            _routingStrategy = routingStrategy;
            _logger = logger;
        }

        /// <summary>
        /// 路由新呼入通话
        /// </summary>
        public async Task<CallRoutingResult> RouteInboundCallAsync(SIPRequest sipRequest)
        {
            try
            {
                _logger.LogInformation($"开始路由新呼入通话 - CallId: {sipRequest.Header.CallId}");

                // 1. 解析被叫号码
                var toUser = sipRequest.Header.To.ToURI.User;
                if (string.IsNullOrEmpty(toUser))
                {
                    return CallRoutingResult.CreateFailure("无法解析被叫号码");
                }

                _logger.LogDebug($"被叫号码: {toUser}");

                // 2. 查找对应用户
                var targetUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.SipUsername == toUser);

                if (targetUser == null)
                {
                    _logger.LogWarning($"未找到对应用户 - SipUsername: {toUser}");
                    return CallRoutingResult.CreateFailure($"用户不存在: {toUser}", CallHandlingStrategy.Fallback);
                }

                // 3. 检查用户在线状态和客户端可用性
                if (!_applicationContext.SipClients.TryGetValue(toUser, out var sipClient))
                {
                    _logger.LogInformation($"用户客户端不在线 - SipUsername: {toUser}");
                    return CallRoutingResult.CreateFailure($"用户不在线: {toUser}", CallHandlingStrategy.Fallback);
                }

                // 4. 检查客户端是否忙碌
                if (sipClient.IsCallActive)
                {
                    _logger.LogInformation($"用户正在通话中 - SipUsername: {toUser}");
                    return CallRoutingResult.CreateFailure($"用户忙碌: {toUser}", CallHandlingStrategy.Fallback);
                }

                // 5. 选择路由策略并返回结果
                var strategy = DetermineCallHandlingStrategy(sipRequest, targetUser, sipClient);
                var result = CallRoutingResult.CreateSuccess(sipClient, targetUser, strategy, $"成功路由到用户: {toUser}");

                _logger.LogInformation($"新呼入路由成功 - SipUsername: {toUser}, Strategy: {strategy}");
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "路由新呼入通话时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 路由呼出应答
        /// </summary>
        public async Task<CallRoutingResult> RouteOutboundResponseAsync(SIPRequest sipRequest)
        {
            try
            {
                var callId = sipRequest.Header.CallId;
                _logger.LogInformation($"开始路由呼出应答 - CallId: {callId}");

                // 1. 根据Call-ID查找对应的呼出记录
                var outboundCallInfo = _callTypeIdentifier.GetOutboundCallInfo(callId);
                if (outboundCallInfo == null)
                {
                    _logger.LogWarning($"未找到对应的呼出记录 - CallId: {callId}");
                    return CallRoutingResult.CreateFailure("未找到对应的呼出记录");
                }

                // 2. 获取发起呼叫的SipClient
                if (!_applicationContext.SipClients.TryGetValue(outboundCallInfo.SipUsername, out var sipClient))
                {
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "路由呼出应答时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 确定通话处理策略
        /// </summary>
        private CallHandlingStrategy DetermineCallHandlingStrategy(SIPRequest sipRequest, User targetUser, AI.Caller.Core.SIPClient sipClient)
        {
            try
            {
                // 分析来电源
                var fromUser = sipRequest.Header.From?.FromURI?.User;
                var isFromWebClient = IsWebClient(fromUser);
                var isToWebClient = IsWebClient(targetUser.SipUsername);

                if (isFromWebClient && isToWebClient)
                {
                    return CallHandlingStrategy.WebToWeb;
                }
                else if (isFromWebClient && !isToWebClient)
                {
                    return CallHandlingStrategy.WebToNonWeb;
                }
                else if (!isFromWebClient && isToWebClient)
                {
                    return CallHandlingStrategy.NonWebToWeb;
                }
                else
                {
                    return CallHandlingStrategy.NonWebToNonWeb;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "确定通话处理策略时发生错误");
                return CallHandlingStrategy.NonWebToWeb; // 默认策略
            }
        }

        /// <summary>
        /// 判断是否为Web客户端
        /// </summary>
        private bool IsWebClient(string? fromUser)
        {
            if (string.IsNullOrEmpty(fromUser))
                return false;

            // 简单的启发式判断：检查是否为已知的Web客户端用户
            return _applicationContext.SipClients.ContainsKey(fromUser);
        }
    }
}