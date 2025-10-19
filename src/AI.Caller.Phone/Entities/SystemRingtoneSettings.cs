namespace AI.Caller.Phone.Entities;

public class SystemRingtoneSettings {
    public int Id { get; set; }

    public int DefaultIncomingRingtoneId { get; set; }

    public int DefaultRingbackToneId { get; set; }

    public int? UpdatedBy { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public Ringtone DefaultIncomingRingtone { get; set; } = null!;
    public Ringtone DefaultRingbackTone { get; set; } = null!;
    public User? UpdatedByUser { get; set; }
}
