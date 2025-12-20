using AI.Caller.Core;
using AI.Caller.Phone.CallRouting.Models;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using SIPSorcery.SIP;

namespace AI.Caller.Phone.Services;

/// <summary>
/// CallManager的CallLog集成扩展
/// 负责在通话生命周期中创建和更新CallLog记录
/// </summary>
public partial class CallManager {
    
    /// <summary>
    /// 创建外呼CallLog记录
    /// </summary>
    private async Task<Entities.CallLog> CreateOutboundCallLogAsync(CallContext ctx, Entities.User caller, string destination) {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var callLog = dbContext.CallLogs.FirstOrDefault(x=>x.CallId == ctx.CallId) ?? new Entities.CallLog {
            CallId = ctx.CallId,
            CallScenario = ctx.Type,
            Direction = Entities.CallDirection.Outbound,
            CallerUserId = caller.Id,
            Status = Entities.CallStatus.InProgress,
            InitiationType = Entities.CallInitiationType.Single,
            StartTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        // 根据场景设置被叫信息
        if (ctx.Type == CallScenario.WebToMobile || ctx.Type == CallScenario.ServerToMobile) {
            callLog.CalleeNumber = destination;
        } else if (ctx.Type == CallScenario.WebToWeb || ctx.Type == CallScenario.ServerToWeb) {
            // 尝试从destination解析用户ID或SIP用户名
            var targetUser = await dbContext.Users
                .Include(u => u.SipAccount)
                .FirstOrDefaultAsync(u => u.SipAccount != null && u.SipAccount.SipUsername == destination);
            
            if (targetUser != null) {
                callLog.CalleeUserId = targetUser.Id;
                callLog.CalleeNumber = destination;
            } else {
                callLog.CalleeNumber = destination;
            }
        } else if (ctx.Type == CallScenario.WebToServer) {
            callLog.CalleeNumber = "AI客服";
        }

        if (callLog.Id > 0) {
            dbContext.CallLogs.Update(callLog);
        } else {
            dbContext.CallLogs.Add(callLog);
        }
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("创建外呼CallLog: CallId={CallId}, Scenario={Scenario}, Caller={CallerUserId}, Callee={Callee}",
            ctx.CallId, ctx.Type, caller.Id, callLog.CalleeNumber ?? callLog.CalleeUserId?.ToString());

        return callLog;
    }

    /// <summary>
    /// 创建来电CallLog记录
    /// </summary>
    private async Task<Entities.CallLog> CreateInboundCallLogAsync(CallContext ctx, CallRoutingResult routingResult) {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var callLog = dbContext.CallLogs.FirstOrDefault(x => x.CallId == ctx.CallId) ?? new Entities.CallLog {
            CallId = ctx.CallId,
            Direction = Entities.CallDirection.Inbound,
            Status = Entities.CallStatus.InProgress,
            InitiationType = Entities.CallInitiationType.Single,
            StartTime = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        if (routingResult.CallerUser != null) {
            callLog.CallerUserId = routingResult.CallerUser.Id;
            callLog.CallerNumber = routingResult.CallerUser.SipAccount?.SipUsername;
        } else {
            callLog.CallerNumber = routingResult.CallerNumber;
        }

        if (routingResult.TargetUser != null) {
            callLog.CalleeUserId = routingResult.TargetUser.Id;
            callLog.CalleeNumber = routingResult.TargetUser.SipAccount?.SipUsername;
        }

        callLog.CallScenario = DetermineCallScenario(routingResult);
        if (callLog.Id > 0) {
            dbContext.CallLogs.Update(callLog);
        } else {
            dbContext.CallLogs.Add(callLog);
        }
        await dbContext.SaveChangesAsync();

        _logger.LogInformation("创建来电CallLog: CallId={CallId}, Scenario={Scenario}, Caller={Caller}, Callee={CalleeUserId}",
            ctx.CallId, callLog.CallScenario, callLog.CallerNumber ?? callLog.CallerUserId?.ToString(), callLog.CalleeUserId);

        return callLog;
    }

    /// <summary>
    /// 更新CallLog记录（通话结束时）
    /// </summary>
    private async Task UpdateCallLogOnEndAsync(string callId, CallFinishStatus finishStatus) {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var callLog = await dbContext.CallLogs
            .FirstOrDefaultAsync(c => c.CallId == callId);

        if (callLog == null) {
            _logger.LogWarning("未找到CallLog记录: CallId={CallId}", callId);
            return;
        }

        callLog.EndTime = DateTime.UtcNow;
        
        if (callLog.StartTime.HasValue) {
            callLog.Duration = callLog.EndTime - callLog.StartTime;
        }

        callLog.FinishStatus = finishStatus;

        // 根据结束状态更新CallStatus
        callLog.Status = finishStatus switch {
            CallFinishStatus.Hangup or CallFinishStatus.RemoteHangUp => Entities.CallStatus.Completed,
            CallFinishStatus.Failed => Entities.CallStatus.Failed,
            CallFinishStatus.Rejected => Entities.CallStatus.Failed,
            CallFinishStatus.Busy => Entities.CallStatus.Failed,
            CallFinishStatus.Cancelled => Entities.CallStatus.Failed,
            _ => Entities.CallStatus.Failed
        };

        callLog.CompletedAt = DateTime.UtcNow;

        // 设置失败原因
        if (callLog.Status == Entities.CallStatus.Failed) {
            callLog.FailureReason = finishStatus switch {
                CallFinishStatus.Failed => "通话失败",
                CallFinishStatus.Rejected => "对方拒接",
                CallFinishStatus.Busy => "对方忙线",
                CallFinishStatus.Cancelled => "已取消",
                _ => "未知原因"
            };
        }

        await dbContext.SaveChangesAsync();

        _logger.LogInformation("更新CallLog: CallId={CallId}, Status={Status}, Duration={Duration}",
            callId, callLog.Status, callLog.Duration);
    }

    /// <summary>
    /// 根据路由结果确定通话场景
    /// </summary>
    private CallScenario DetermineCallScenario(CallRoutingResult routingResult) {
        // 判断是否启用AI客服
        using var scope = _serviceScopeFactory.CreateScope();
        var aiSettingsProvider = scope.ServiceProvider.GetRequiredService<IAICustomerServiceSettingsProvider>();
        var aiSettings = aiSettingsProvider.GetSettingsAsync().Result;

        if (aiSettings.Enabled) {
            // AI客服启用
            if (routingResult.Strategy == CallHandlingStrategy.WebToWeb) {
                return CallScenario.WebToServer;
            } else if (routingResult.Strategy == CallHandlingStrategy.NonWebToWeb) {
                return CallScenario.MobileToServer;
            }
        } else {
            // AI客服未启用
            if (routingResult.Strategy == CallHandlingStrategy.WebToWeb) {
                return CallScenario.WebToWeb;
            } else if (routingResult.Strategy == CallHandlingStrategy.NonWebToWeb) {
                return CallScenario.MobileToWeb;
            }
        }

        // 默认返回MobileToWeb
        return CallScenario.MobileToWeb;
    }
}
