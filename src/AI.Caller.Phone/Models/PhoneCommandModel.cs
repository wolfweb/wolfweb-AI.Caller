using AI.Caller.Core;

namespace AI.Caller.Phone.Models;

public enum PhoneCommandType {
    Register,
    Call,
    HangUp,
    SendDtmf,
    Answer
}

public record PhoneCommandModel(string Name, SIPClient Client, PhoneCommandType Command);
