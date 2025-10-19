namespace AI.Caller.Phone.Entities;

public class UserRingtoneSettings {
    public int Id { get; set; }

    public int UserId { get; set; }

    public int? IncomingRingtoneId { get; set; }

    public int? RingbackToneId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Ringtone? IncomingRingtone { get; set; }
    public Ringtone? RingbackTone { get; set; }
}
