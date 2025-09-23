namespace AI.Caller.Phone.CallRouting.Models {
    public enum CallHandlingStrategy {
        Reject,
        WebToWeb,
        WebToNonWeb,
        NonWebToWeb,
        Fallback
    }
}