namespace AI.Caller.Phone.Entities;

public class SipSetting {
    public int Id { get; set; }
    public string? SipServer { get; set; }
    public DateTime CreateAt { get; set; } = DateTime.UtcNow;
}