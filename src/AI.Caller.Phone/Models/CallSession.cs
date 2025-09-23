namespace AI.Caller.Phone.Models {
    public class CallSession {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public CallContext PrimaryCall { get; set; } = new();
        public CallContext? SecondaryCall { get; set; } // 用于转接或会议
        public DateTime SessionStart { get; set; } = DateTime.UtcNow;
        public SessionState State { get; set; } = SessionState.Active;
    }
}