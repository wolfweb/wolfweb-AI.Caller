using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.CallRouting.Services {
    public class CallRoutingService : ICallRoutingService {
        private readonly ILogger _logger;
        private readonly AppDbContext _dbContext;
        private readonly ICallManager _callManager;
        private readonly ApplicationContext _applicationContext;

        public CallRoutingService(
            AppDbContext dbContext,
            ICallManager callManager,
            ILogger<CallRoutingService> logger,
            ApplicationContext applicationContext
            ) {
            _logger             = logger;
            _dbContext          = dbContext;
            _callManager        = callManager;
            _applicationContext = applicationContext;
        }

        public async Task<CallRoutingResult> RouteInboundCallAsync(string toUser, SIPRequest sipRequest) {
            try {
                var targetUsers = await _dbContext.Users.Include(u => u.SipAccount).Where(u => u.SipAccount != null && u.SipAccount.SipUsername == toUser).ToArrayAsync();
                User? targetUser;
                var callingUsers = _callManager.GetActiviteUsers().Select(x => x.Id).ToArray();
                var inactiveUsers = _applicationContext.GetInactiveUsers();
                if (targetUsers == null || targetUsers.Length ==0) {
                    var validUsers = inactiveUsers.Where(x => callingUsers.All(y => y != x));
                    targetUser = _dbContext.Users.Include(u => u.SipAccount).FirstOrDefault(x=> validUsers.Contains(x.Id));
                    if (targetUser != null) {
                        _logger.LogInformation($"未找到用户信息，使用默认坐席 - Id: {targetUser.Id}");
                    } else {
                        _logger.LogWarning($"未找到用户信息且没有可用客户端 : {toUser}");
                        return CallRoutingResult.CreateFailure($"未找到用户: {toUser}", CallHandlingStrategy.Reject);
                    }
                } else {
                    var targetUserIds = targetUsers.Where(x => callingUsers.All(m => m != x.Id)).Select(x=>x.Id).ToArray();
                    var finded = inactiveUsers.FirstOrDefault(x => targetUserIds.Contains(x));
                    if (finded == 0) {
                        _logger.LogInformation($"用户客户端无可用坐席 - SipUsername: {toUser}");
                        return CallRoutingResult.CreateFailure($"用户客户端无可用坐席: {toUser}", CallHandlingStrategy.Fallback);
                    }

                    targetUser = targetUsers.First(u => u.Id == finded);
                }

                var (strategy, caller, callee) = DetermineCallHandlingStrategy(sipRequest, targetUser);
                var result = CallRoutingResult.CreateSuccess(targetUser, strategy, $"成功路由到用户: {toUser}");

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

        private (CallHandlingStrategy, User?, User?) DetermineCallHandlingStrategy(SIPRequest sipRequest, User targetUser) {
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
                    return (CallHandlingStrategy.Fallback, caller, callee);
                }
            } catch (Exception ex) {
                _logger.LogError(ex, "确定通话处理策略时发生错误");
                return (CallHandlingStrategy.NonWebToWeb, caller, callee);
            }
        }

        private (bool, User?) IsWebClient(string? fromUser) {
            if (string.IsNullOrEmpty(fromUser))
                return (false, null);

            try {
                var user = _dbContext.Users.Include(x => x.SipAccount).SingleOrDefault(u => u.Username == fromUser);
                return (user != null, user);
            } catch {
                return (false, null);
            }
        }
    }
}
