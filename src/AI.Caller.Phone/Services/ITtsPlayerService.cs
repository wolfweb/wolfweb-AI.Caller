using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Services;

using AI.Caller.Phone.Entities;

public interface ITtsPlayerService {
    Task<(TtsTemplate? template, bool success)> PlayAsync(CallContext callContext, string destination);
}