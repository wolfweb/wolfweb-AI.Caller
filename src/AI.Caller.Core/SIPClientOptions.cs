namespace AI.Caller.Core {
    public class SIPClientOptions {
        public string? SIPServer { get; set; }
        public string? SIPUsername { get; set; }
        public string? SIPPassword { get; set; }
        public string? SIPFromName { get; set; }
        public int AudioOutDeviceIndex { get; set; } = 0;
    }
}
