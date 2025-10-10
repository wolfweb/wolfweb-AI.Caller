using AI.Caller.Core;
using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;

namespace AI.Caller.Phone.Models {
    public class CallContext {
        public string        CallId { get; } = $"AI_Caller_{UniqueShortStringProvider.Create()}"; 
        public CallScenario  Type   { get; set; }
        public Caller?       Caller { get; set; }
        public Callee?       Callee { get; set; }

        public DateTime      CreatedAt     { get; set; } = DateTime.UtcNow;
        public CallState     State         { get; set; } = CallState.Initiating;        
        
        public bool          IsActive => State != CallState.Ended && State != CallState.Failed;
        public TimeSpan      Duration => DateTime.UtcNow - CreatedAt;
    }

    public class Caller {
        public User?                User         { get; set; }
        public string?              Number       { get; set; }
        public SIPClientHandle?     Client       { get; set; }
        public MediaSessionManager? MediaManager => Client?.Client?.MediaSessionManager;
    }

    public class Callee {
        public User?                User         { get; set; }
        public string?              Number       { get; set; }
        public SIPClientHandle?     Client       { get; set; }
        public MediaSessionManager? MediaManager => Client?.Client?.MediaSessionManager;
    }
}