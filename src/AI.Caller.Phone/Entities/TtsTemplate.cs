using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities;

public class TtsTemplate {
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [StringLength(100)]
    public string? TargetPattern { get; set; }

    public int Priority { get; set; } = 0;

    public bool IsActive { get; set; } = true;
}