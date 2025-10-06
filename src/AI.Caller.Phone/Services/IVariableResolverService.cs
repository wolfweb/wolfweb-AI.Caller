using AI.Caller.Phone.Models;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

public interface IVariableResolverService {
    Task<string> ResolveVariablesAsync(string text, CallContext context);
}