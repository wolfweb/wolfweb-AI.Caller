using AI.Caller.Phone.Models;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

public interface ICallFlowOrchestrator {
    Task HandleInboundCallAsync(CallContext callContext);
}