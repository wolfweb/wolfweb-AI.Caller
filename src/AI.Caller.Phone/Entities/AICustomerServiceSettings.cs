using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    public class AICustomerServiceSettings {
        [Key]
        public int    Id                   { get; set; }
        [Display(Name = "启用 AI 客服")]
        public bool   Enabled              { get; set; } = false;
        [Display(Name = "默认欢迎语")]
        public string DefaultWelcomeScript { get; set; } = "您好，欢迎致电，我是AI客服助手。";
        [Display(Name = "自动应答延迟(毫秒)")]
        public int    AutoAnswerDelayMs    { get; set; } = 2000;
        [Display(Name = "默认说话人 ID")]
        public int    DefaultSpeakerId     { get; set; } = 0;
        [Display(Name = "默认语速")]
        public float  DefaultSpeed         { get; set; } = 1.0f;

        [Display(Name = "默认TTS应答模板")]
        public int? DefaultTtsTemplateId { get; set; }
    }
}