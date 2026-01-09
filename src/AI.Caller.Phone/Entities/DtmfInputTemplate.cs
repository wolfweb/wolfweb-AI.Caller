using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// DTMF输入模板
    /// </summary>
    public class DtmfInputTemplate {
        [Key]
        public int Id { get; set; }

        [Display(Name = "模板名称")]
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Display(Name = "输入类型")]
        [Required]
        public DtmfInputType InputType { get; set; }

        [Display(Name = "验证器类型")]
        [Required]
        [MaxLength(100)]
        public string ValidatorType { get; set; } = string.Empty;

        [Display(Name = "最大长度")]
        [Required]
        public int MaxLength { get; set; } = 18;

        [Display(Name = "最小长度")]
        public int MinLength { get; set; } = 1;

        [Display(Name = "终止键")]
        public char TerminationKey { get; set; } = '#';

        [Display(Name = "退格键")]
        public char BackspaceKey { get; set; } = '*';

        [Display(Name = "提示文本")]
        [Required]
        [MaxLength(500)]
        public string PromptText { get; set; } = string.Empty;

        [Display(Name = "成功提示")]
        [MaxLength(500)]
        public string? SuccessText { get; set; }

        [Display(Name = "错误提示")]
        [MaxLength(500)]
        public string? ErrorText { get; set; }

        [Display(Name = "超时提示")]
        [MaxLength(500)]
        public string? TimeoutText { get; set; }

        [Display(Name = "最大重试次数")]
        public int MaxRetries { get; set; } = 3;

        [Display(Name = "超时时间(秒)")]
        public int TimeoutSeconds { get; set; } = 30;

        [Display(Name = "按键映射(JSON)")]
        public string? InputMappingJson { get; set; }

        [Display(Name = "创建时间")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Display(Name = "更新时间")]
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
