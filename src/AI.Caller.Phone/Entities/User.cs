namespace AI.Caller.Phone.Entities;
public class User
{
    public int       Id             { get; set; }
    public string?   Username       { get; set; }
    public string?   Password       { get; set; }
                                    
    // SIP账号信息                   
    public string?   SipUsername    { get; set; }
    public string?   SipPassword    { get; set; }
    public bool      SipRegistered  { get; set; }
    public DateTime? RegisteredAt   { get; set; } 

    public ICollection<Contact>? Contacts { get; set; }
}
