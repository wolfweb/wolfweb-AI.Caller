using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities {
    /// <summary>
    /// DTMF输入模板
    /// </summary>
    public class DtmfInputTemplate {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public DtmfInputType InputType { get; set; }

        [Required]
        [MaxLength(100)]
        public string ValidatorType { get; set; } = string.Empty;

        [Required]
        public int MaxLength { get; set; } = 18;

        public int MinLength { get; set; } = 1;

        public char TerminationKey { get; set; } = '#';

        public char BackspaceKey { get; set; } = '*';

        [Required]
        [MaxLength(500)]
        public string PromptText { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? SuccessText { get; set; }

        [MaxLength(500)]
        public string? ErrorText { get; set; }

        [MaxLength(500)]
        public string? TimeoutText { get; set; }

        public int MaxRetries { get; set; } = 3;

        public int TimeoutSeconds { get; set; } = 30;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
