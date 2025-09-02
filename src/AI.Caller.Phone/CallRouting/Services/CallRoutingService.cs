﻿﻿using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;
using AI.Caller.Phone.CallRouting.Interfaces;
using AI.Caller.Phone.CallRouting.Models;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Entities;
using System.Linq;
using AI.Caller.Core;

namespace AI.Caller.Phone.CallRouting.Services {
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
            _callTypeIdentifier = callTypeIdentifier;
            _routingStrategy = routingStrategy;
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<CallRoutingResult> RouteInboundCallAsync(SIPRequest sipRequest) {
            try {
                _logger.LogInformation($"开始路由新呼入通话 - CallId: {sipRequest.Header.CallId}");

                var toUser = sipRequest.Header.To.ToURI.User;
                if (string.IsNullOrEmpty(toUser)) {
                    return CallRoutingResult.CreateFailure("无法解析被叫号码");
                }

                _logger.LogDebug($"被叫号码: {toUser}");

                var targetUsers = await _dbContext.Users.Include(u => u.SipAccount).Where(u => u.SipAccount != null && u.SipAccount.SipUsername == toUser).ToArrayAsync();
                SIPClient? sipClient;
                User? targetUser;
                if (targetUsers == null || targetUsers.Length ==0) {
                    var client = _applicationContext.SipClients.FirstOrDefault(x => !x.Value.IsCallActive);
                    if (client.Value != null) {
                        sipClient = client.Value;
                        targetUser = await _dbContext.Users.FirstAsync(u => u.Id == client.Key);
                        _logger.LogInformation($"未找到用户信息，使用默认坐席 - Id: {client.Key}");
                    } else {
                        _logger.LogWarning($"未找到用户信息且没有可用客户端 : {toUser}");
                        return CallRoutingResult.CreateFailure($"未找到用户: {toUser}", CallHandlingStrategy.Reject);
                    }
                } else {
                    var targetUserIds = targetUsers.Select(x => x.Id).ToArray();
                    var finded = _applicationContext.SipClients.FirstOrDefault(x => targetUserIds.Contains(x.Key) && !x.Value.IsCallActive);
                    if (finded.Value == null) {
                        _logger.LogInformation($"用户客户端不在线 - SipUsername: {toUser}");
                        return CallRoutingResult.CreateFailure($"用户不在线: {toUser}", CallHandlingStrategy.Fallback);
                    }

                    targetUser = targetUsers.First(u => u.Id == finded.Key);
                    sipClient = finded.Value;
                }

                if (sipClient.IsCallActive) {
                    _logger.LogInformation($"用户正在通话中 - SipUsername: {toUser}");
                    return CallRoutingResult.CreateFailure($"用户忙碌: {toUser}", CallHandlingStrategy.Fallback);
                }

                var (strategy, caller, callee) = DetermineCallHandlingStrategy(sipRequest, targetUser, sipClient);
                var result = CallRoutingResult.CreateSuccess(sipClient, targetUser, strategy, $"成功路由到用户: {toUser}");

                result.CallerUser = caller;
                if(result.CallerUser == null) {
                    result.CallerNumber = sipRequest.Header.From?.FromURI?.User;
                }

                _logger.LogInformation($"新呼入路由成功 - from: {caller?.Username ?? result.CallerNumber} to: {toUser}, Strategy: {strategy}");
                return result;
            } catch (Exception ex) {
                _logger.LogError(ex, "路由新呼入通话时发生错误");
                return CallRoutingResult.CreateFailure($"路由失败: {ex.Message}");
            }
        }

        private (CallHandlingStrategy, User?, User?) DetermineCallHandlingStrategy(SIPRequest sipRequest, User targetUser, AI.Caller.Core.SIPClient sipClient) {
            var fromUser = sipRequest.Header.From?.FromName;
            var (isFromWebClient, caller) = IsWebClient(fromUser);
            var (isToWebClient, callee) = (true, targetUser);
            try {
                if (isFromWebClient && isToWebClient) {
                    return (CallHandlingStrategy.WebToWeb, caller, callee);
                } else if (isFromWebClient && !isToWebClient) {
                    return (CallHandlingStrategy.WebToNonWeb, caller, callee);
                } else if (!isFromWebClient && isToWebClient) {
                    return (CallHandlingStrategy.NonWebToWeb, caller, callee);
                } else {
                    return (CallHandlingStrategy.NonWebToNonWeb, caller, callee);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "确定通话处理策略时发生错误");
                return (CallHandlingStrategy.NonWebToWeb, caller, callee);
            }
        }

        public async Task<CallRoutingResult> RouteOutboundCallAsync(OutboundCallInfo outboundCallInfo) {
            try {
                _logger.LogInformation($"开始路由外呼请求 - SipUsername: {outboundCallInfo.SipUsername}, Destination: {outboundCallInfo.Destination}");

                var callerUser = await _dbContext.Users
                    .FirstOrDefaultAsync(u => u.SipAccount != null && u.SipAccount.SipUsername == outboundCallInfo.SipUsername);

                if (callerUser == null || !_applicationContext.SipClients.TryGetValue(callerUser.Id, out var sipClient)) {
                    _logger.LogWarning($"发起呼叫的客户端不存在 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"发起呼叫的客户端不存在: {outboundCallInfo.SipUsername}");
                }

                if (callerUser == null) {
                    _logger.LogWarning($"发起呼叫的用户不存在 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"发起呼叫的用户不存在: {outboundCallInfo.SipUsername}");
                }

                if (sipClient.IsCallActive) {
                    _logger.LogInformation($"发起呼叫的用户正在通话中 - SipUsername: {outboundCallInfo.SipUsername}");
                    return CallRoutingResult.CreateFailure($"用户忙碌: {outboundCallInfo.SipUsername}", CallHandlingStrategy.Fallback);
                }


                CallHandlingStrategy strategy;
                if (IsPstnNumber(outboundCallInfo.Destination)) {
                    strategy = CallHandlingStrategy.WebToNonWeb; // Web到PSTN
                } else {
                    var targetUser = await _dbContext.Users.Include(u => u.SipAccount).FirstOrDefaultAsync(u => u.SipAccount != null && u.SipAccount.SipUsername == outboundCallInfo.Destination);
                    if (targetUser != null && _applicationContext.SipClients.ContainsKey(targetUser.Id)) {
                        strategy = CallHandlingStrategy.WebToWeb; // Web到Web
                    } else {
                        strategy = CallHandlingStrategy.WebToNonWeb; // 默认为Web到非Web
                    }
                }

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

        private static bool IsPstnNumber(string number) {
            if (string.IsNullOrEmpty(number))
                return false;

            return number.All(c => char.IsDigit(c) || c == '+') &&
                   number.Length >= 10 &&
                   number.Length <= 15;
        }

        private (bool, User?) IsWebClient(string? fromUser) {
            if (string.IsNullOrEmpty(fromUser))
                return (false, null);

            try {
                var user = _dbContext.Users.Include(x => x.SipAccount).SingleOrDefault(u => u.Username == fromUser);
                return (user != null && _applicationContext.SipClients.ContainsKey(user.Id), user);
            } catch {
                return (false, null);
            }
        }
    }
}
