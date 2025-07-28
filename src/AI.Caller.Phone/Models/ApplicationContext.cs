using AI.Caller.Core;
using SIPSorcery.Net;
using System.Collections.Concurrent;

namespace AI.Caller.Phone.Models;

public class ApplicationContext {
    public string SipServer { get; set; } = default!;
    public DateTime StartAt { get; set; } = DateTime.UtcNow;
    public ConcurrentDictionary<string, SIPClient> SipClients { get; set; }  = new();
}
