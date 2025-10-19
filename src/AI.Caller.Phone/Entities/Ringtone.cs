using System.ComponentModel.DataAnnotations;

namespace AI.Caller.Phone.Entities;

public class Ringtone {
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int Duration { get; set; }

    [Required]
    [MaxLength(20)]
    public string Type { get; set; } = string.Empty;

    public bool IsSystem { get; set; }

    public int? UploadedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? Uploader { get; set; }
}

public enum RingtoneType {
    Incoming,
    Ringback,
    Both
}