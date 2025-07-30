using AI.Caller.Core;
using SIPSorcery.Net;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    public string SipServer { get; set; } = default!;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<string, SIPClient> SipClients { get; set; }  = new();

    public event Action<string, SIPClient>? OnSipClientAdded;
    public event Action<string, SIPClient>? OnSipClientRemoved;

    public void AddSipClient(string sipUsername, SIPClient sipClient)
    {
        SipClients[sipUsername] = sipClient;
        OnSipClientAdded?.Invoke(sipUsername, sipClient);
    }

    public bool RemoveSipClient(string sipUsername)
    {
        if (SipClients.TryRemove(sipUsername, out var sipClient))
        {
            OnSipClientRemoved?.Invoke(sipUsername, sipClient);
            return true;
        }
        return false;
    }
}
