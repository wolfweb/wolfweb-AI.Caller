namespace AI.Caller.Phone.Models.Dto;

public class RingtoneDto {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Duration { get; set; }
    public string Type { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public int? UploadedBy { get; set; }
    public string? UploaderName { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UserRingtoneSettingsDto {
    public int UserId { get; set; }
    public RingtoneDto? IncomingRingtone { get; set; }
    public RingtoneDto? RingbackTone { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class UpdateUserRingtoneSettingsDto {
    public int? IncomingRingtoneId { get; set; }
    public int? RingbackToneId { get; set; }
}

public class UpdateSystemRingtoneDto {
    public int DefaultIncomingRingtoneId { get; set; }
    public int DefaultRingbackToneId { get; set; }
}

public class UploadRingtoneDto {
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = "Both";
}
