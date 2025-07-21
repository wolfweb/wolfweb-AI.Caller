namespace AI.Caller.Phone.Entities;
public class Contact
{
    public int Id { get; set; }
    public string? Name { get; set; }
    public string? PhoneNumber { get; set; }
    public User? User { get; set; }
    public int UserId { get; set; }
}
