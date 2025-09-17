namespace AI.Caller.Core {
    public class AICustomerServiceSettings {        
        public bool Enabled { get; set; } = false;
        
        public bool AutoAnswerInbound { get; set; } = false;
        
        public bool AutoStartOnOutbound { get; set; } = false;
        
        public bool AutoStartOnInbound { get; set; } = false;
        
        public string DefaultWelcomeScript { get; set; } = "您好，欢迎致电，我是AI客服助手。";
        
        public int AutoAnswerDelayMs { get; set; } = 2000;
        
        public int DefaultSpeakerId { get; set; } = 0;
        
        public float DefaultSpeed { get; set; } = 1.0f;
    }
}