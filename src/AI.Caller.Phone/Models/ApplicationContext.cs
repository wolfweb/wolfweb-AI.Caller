using System.Collections.Concurrent;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    private readonly TimeSpan _defaultInactivityThreshold = TimeSpan.FromMinutes(1);

    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<int, UserSession> UserSessions { get; set; } = new();

    public ApplicationContext() {
    }

    public void AddActiviteUser(int userId) {
        UserSessions.AddOrUpdate(userId,
            new UserSession {
                UserId       = userId,
                IsOnline     = true,
                LastActivity = DateTime.UtcNow,
            },
            (key, existing) => {
                existing.LastActivity = DateTime.UtcNow;
                existing.IsOnline = true;
                return existing;
            });
    }

    public bool RemoveActiviteUserId(int userId) {
        if (UserSessions.TryRemove(userId, out var session)) {
            session.IsOnline = false;
            session.LastActivity = DateTime.UtcNow;
            return true;
        }
        return false;
    }

    public void UpdateUserActivity(int userId) {
        if (UserSessions.TryGetValue(userId, out var session)) {
            session.LastActivity = DateTime.UtcNow;
        }
    }

    public List<int> GetInactiveUsers(TimeSpan? inactivityThreshold = null) {
        var cutoffTime = DateTime.UtcNow - (inactivityThreshold ?? _defaultInactivityThreshold);
        return UserSessions
            .Where(kvp => !kvp.Value.IsOnline || kvp.Value.LastActivity < cutoffTime)
            .Select(kvp => kvp.Value.UserId)
            .ToList();
    }
}

public class UserSession {
    public int      UserId       { get; set; }
    public bool     IsOnline     { get; set; }
    public DateTime LastActivity { get; set; }
    public string?  ConnectionId { get; set; } 
    public DateTime CreatedAt    { get; set; } = DateTime.UtcNow;
}
