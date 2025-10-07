namespace AI.Caller.Phone.Models;

public class CallResult {
    public CallOutcome Status { get; set; }
    public string? FailureReason { get; set; }

    public static CallResult Success() => new() { Status = CallOutcome.Completed };
    public static CallResult Failed(string reason) => new() { Status = CallOutcome.Failed, FailureReason = reason };
}

public enum CallOutcome {
    Completed,
    Failed
}