using AI.Caller.Core;
using SIPSorcery.Net;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    public string SipServer { get; set; } = default!;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<string, SIPClient> SipClients { get; set; }  = new();

    // 事件通知
    public event Action<string, SIPClient>? OnSipClientAdded;
    public event Action<string, SIPClient>? OnSipClientRemoved;

    /// <summary>
    /// 添加SIP客户端并触发事件
    /// </summary>
    public void AddSipClient(string sipUsername, SIPClient sipClient)
    {
        SipClients[sipUsername] = sipClient;
        OnSipClientAdded?.Invoke(sipUsername, sipClient);
    }

    /// <summary>
    /// 移除SIP客户端并触发事件
    /// </summary>
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
