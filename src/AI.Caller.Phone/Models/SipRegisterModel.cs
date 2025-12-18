using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Models;

public record SipRegisterModel(SipAccount? SipAccount = null, User? User = null);