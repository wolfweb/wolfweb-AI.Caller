using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Caller.Phone.Services;

/// <summary>
/// 监听服务实现
/// </summary>
public class MonitoringService : IMonitoringService {
    private readonly AppDbContext _dbContext;
    private readonly ILogger<MonitoringService> _logger;

    public MonitoringService(AppDbContext dbContext, ILogger<MonitoringService> logger) {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<MonitoringSession> StartMonitoringAsync(string callId, int monitorUserId, string monitorUserName) {
        try {
            // 检查是否已有活跃的监听会话
            var existingSession = await _dbContext.MonitoringSessions
                .FirstOrDefaultAsync(s => s.CallId == callId && s.IsActive);

            if (existingSession != null) {
                _logger.LogWarning("通话已存在活跃的监听会话: {CallId}, SessionId: {SessionId}",
                    callId, existingSession.Id);
                throw new MonitoringSessionAlreadyExistsException(callId);
            }

            var session = new MonitoringSession {
                CallId = callId,
                MonitorUserId = monitorUserId,
                MonitorUserName = monitorUserName,
                StartTime = DateTime.UtcNow,
                IsActive = true
            };

            _dbContext.MonitoringSessions.Add(session);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("监听会话已开始: {SessionId}, CallId: {CallId}, User: {UserId}",
                session.Id, callId, monitorUserId);

            return session;
        } catch (MonitoringSessionAlreadyExistsException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "开始监听失败: CallId {CallId}, User {UserId}", callId, monitorUserId);
            throw;
        }
    }

    public async Task StopMonitoringAsync(int sessionId) {
        try {
            var session = await _dbContext.MonitoringSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null) {
                throw new MonitoringSessionNotFoundException(sessionId);
            }

            session.EndTime = DateTime.UtcNow;
            session.IsActive = false;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("监听会话已停止: {SessionId}, CallId: {CallId}",
                sessionId, session.CallId);
        } catch (MonitoringSessionNotFoundException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "停止监听失败: SessionId {SessionId}", sessionId);
            throw;
        }
    }

    public async Task InterventionAsync(int sessionId, string reason) {
        try {
            var session = await _dbContext.MonitoringSessions
                .FirstOrDefaultAsync(s => s.Id == sessionId);

            if (session == null) {
                throw new MonitoringSessionNotFoundException(sessionId);
            }

            if (!session.IsActive) {
                _logger.LogWarning("监听会话已结束，无法接入: {SessionId}", sessionId);
                throw new InterventionException(session.CallId, "监听会话已结束");
            }

            session.InterventionTime = DateTime.UtcNow;
            session.InterventionReason = reason;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("人工接入已记录: {SessionId}, CallId: {CallId}, Reason: {Reason}",
                sessionId, session.CallId, reason);
        } catch (MonitoringSessionNotFoundException) {
            throw;
        } catch (InterventionException) {
            throw;
        } catch (Exception ex) {
            _logger.LogError(ex, "人工接入失败: SessionId {SessionId}", sessionId);
            throw;
        }
    }

    public async Task<List<MonitoringSession>> GetActiveSessionsAsync() {
        try {
            return await _dbContext.MonitoringSessions
                .Where(s => s.IsActive)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取活跃监听会话失败");
            throw;
        }
    }

    public async Task<List<MonitoringSession>> GetUserSessionsAsync(int userId) {
        try {
            return await _dbContext.MonitoringSessions
                .Where(s => s.MonitorUserId == userId)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取用户监听会话失败: UserId {UserId}", userId);
            throw;
        }
    }

    public async Task<List<MonitoringSession>> GetCallSessionsAsync(string callId) {
        try {
            return await _dbContext.MonitoringSessions
                .Where(s => s.CallId == callId)
                .OrderByDescending(s => s.StartTime)
                .ToListAsync();
        } catch (Exception ex) {
            _logger.LogError(ex, "获取通话监听会话失败: CallId {CallId}", callId);
            throw;
        }
    }

    public async Task<bool> IsCallBeingMonitoredAsync(string callId) {
        try {
            return await _dbContext.MonitoringSessions
                .AnyAsync(s => s.CallId == callId && s.IsActive);
        } catch (Exception ex) {
            _logger.LogError(ex, "检查通话监听状态失败: CallId {CallId}", callId);
            throw;
        }
    }
}
