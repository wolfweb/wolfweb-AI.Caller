namespace AI.Caller.Phone.Models {
    public class HangupRetryPolicy {
        public int MaxRetries { get; set; } = 3;

        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

        public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan HangupTimeout { get; set; } = TimeSpan.FromSeconds(10);

        public TimeSpan NotificationTimeout { get; set; } = TimeSpan.FromSeconds(5);
    }
}