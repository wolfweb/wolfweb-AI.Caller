using AI.Caller.Core;
using SIPSorcery.Net;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    public string SipServer { get; set; } = default!;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<string, SIPClient> SipClients { get; set; } = new();

    public ConcurrentDictionary<string, UserSession> UserSessions { get; set; } = new();

    public event Action<string, SIPClient>? OnSipClientAdded;
    public event Action<string, SIPClient>? OnSipClientRemoved;

    public void AddSipClient(string sipUsername, SIPClient sipClient) {
        SipClients[sipUsername] = sipClient;

        UserSessions.AddOrUpdate(sipUsername,
            new UserSession {
                SipUsername = sipUsername,
                LastActivity = DateTime.UtcNow,
                IsOnline = true
            },
            (key, existing) => {
                existing.LastActivity = DateTime.UtcNow;
                existing.IsOnline = true;
                return existing;
            });

        OnSipClientAdded?.Invoke(sipUsername, sipClient);
    }

    public bool RemoveSipClient(string sipUsername) {
        if (SipClients.TryRemove(sipUsername, out var sipClient)) {
            try {
                sipClient.Shutdown();
            } catch (Exception ex) {
                Console.WriteLine($"Error shutting down SIPClient for {sipUsername}: {ex.Message}");
            }

            if (UserSessions.TryGetValue(sipUsername, out var session)) {
                session.IsOnline = false;
                session.LastActivity = DateTime.UtcNow;
            }

            OnSipClientRemoved?.Invoke(sipUsername, sipClient);
            return true;
        }
        return false;
    }

    public void UpdateUserActivity(string sipUsername) {
        if (UserSessions.TryGetValue(sipUsername, out var session)) {
            session.LastActivity = DateTime.UtcNow;
        }
    }

    public List<string> GetInactiveUsers(TimeSpan inactivityThreshold) {
        var cutoffTime = DateTime.UtcNow - inactivityThreshold;
        return UserSessions
            .Where(kvp => !kvp.Value.IsOnline || kvp.Value.LastActivity < cutoffTime)
            .Select(kvp => kvp.Key)
            .ToList();
    }
}

public class UserSession {
    public string SipUsername { get; set; } = string.Empty;
    public DateTime LastActivity { get; set; }
    public bool IsOnline { get; set; }
    public string? ConnectionId { get; set; } // SignalR连接ID
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
