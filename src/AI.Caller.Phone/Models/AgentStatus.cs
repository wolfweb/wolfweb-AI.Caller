namespace AI.Caller.Phone.Models {
    public enum AgentStatus {
        Offline = 0, 
        OnlineIdle = 1, 
        OnlineBusy = 2, 
        OnlineAfterCall = 3,
        OnlinePaused = 4, 
        OnlineBreak = 5, 
        AI_Online = 10, 
        AI_Busy = 11,
        AI_Maintenance = 12, 
        AI_Overloaded = 13
    }
}