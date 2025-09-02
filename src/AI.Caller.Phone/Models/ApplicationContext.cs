using AI.Caller.Core;
using SIPSorcery.Net;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    private readonly IServiceProvider _serviceProvider;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;

    public ConcurrentDictionary<int, SIPClient> SipClients { get; set; } = new();
    public ConcurrentDictionary<int, UserSession> UserSessions { get; set; } = new();

    public event Action<int, SIPClient>? OnSipClientAdded;
    public event Action<int, SIPClient>? OnSipClientRemoved;

    public ApplicationContext(IServiceProvider serviceProvider) {
        _serviceProvider = serviceProvider;
    }

    public void AddSipClientByUserId(int userId, SIPClient sipClient) {
        SipClients[userId] = sipClient;
        UserSessions.AddOrUpdate(userId,
            new UserSession {
                UserId = userId,
                LastActivity = DateTime.UtcNow,
                IsOnline = true
            },
            (key, existing) => {
                existing.LastActivity = DateTime.UtcNow;
                existing.IsOnline = true;
                return existing;
            });

        OnSipClientAdded?.Invoke(userId, sipClient);
    }

    public bool RemoveSipClientByUserId(int userId) {
        if (SipClients.TryRemove(userId, out var sipClient)) {
            try {
                sipClient.Shutdown();
            } catch (Exception ex) {
                Console.WriteLine($"Error shutting down SIPClient for userId {userId}: {ex.Message}");
            }

            if (UserSessions.TryGetValue(userId, out var session)) {
                session.IsOnline = false;
                session.LastActivity = DateTime.UtcNow;

                OnSipClientRemoved?.Invoke(session.UserId, sipClient);
            }

            return true;
        }
        return false;
    }

    public SIPClient? GetSipClientByUserId(int userId) {
        SipClients.TryGetValue(userId, out var sipClient);
        return sipClient;
    }

    public void UpdateUserActivityByUserId(int userId) {
        if (UserSessions.TryGetValue(userId, out var session)) {
            session.LastActivity = DateTime.UtcNow;
        }
    }

    public List<int> GetInactiveUsers(TimeSpan inactivityThreshold) {
        var cutoffTime = DateTime.UtcNow - inactivityThreshold;
        return UserSessions
            .Where(kvp => !kvp.Value.IsOnline || kvp.Value.LastActivity < cutoffTime)
            .Select(kvp => kvp.Value.UserId)
            .ToList();
    }

    public List<int> GetInactiveUserIds(TimeSpan inactivityThreshold) {
        var cutoffTime = DateTime.UtcNow - inactivityThreshold;
        return UserSessions
            .Where(kvp => !kvp.Value.IsOnline || kvp.Value.LastActivity < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    public Dictionary<int, SIPClient> GetAllSipClients() {
        return new Dictionary<int, SIPClient>(SipClients);
    }

    public UserSession? GetUserSession(int userId) {
        UserSessions.TryGetValue(userId, out var session);
        return session;
    }

    public void AddSipClient(int userId, SIPClient sipClient) {
        SipClients[userId] = sipClient;

        if (!UserSessions.ContainsKey(userId)) {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = dbContext.Users.Include(u => u.SipAccount).FirstOrDefault(u => u.Id == userId);

            if (user != null) {
                UserSessions[userId] = new UserSession {
                    UserId = userId,
                    IsOnline = true,
                    LastActivity = DateTime.UtcNow
                };
            }
        } else {
            UserSessions[userId].IsOnline = true;
            UserSessions[userId].LastActivity = DateTime.UtcNow;
        }

        OnSipClientAdded?.Invoke(userId, sipClient);
    }

    public bool RemoveSipClient(int userId) {
        return RemoveSipClientByUserId(userId);
    }
}

public class UserSession {
    public int UserId { get; set; }
    public bool IsOnline { get; set; }
    public DateTime LastActivity { get; set; }
    public string? ConnectionId { get; set; } 
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
