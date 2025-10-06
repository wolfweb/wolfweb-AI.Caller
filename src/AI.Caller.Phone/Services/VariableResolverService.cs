using AI.Caller.Phone.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Threading.Tasks;

namespace AI.Caller.Phone.Services;

public class VariableResolverService : IVariableResolverService {
    private readonly ILogger<VariableResolverService> _logger;

    public VariableResolverService(ILogger<VariableResolverService> logger) {
        _logger = logger;
    }

    public Task<string> ResolveVariablesAsync(string text, CallContext context) {
        _logger.LogDebug("Resolving variables for text: '{Text}'", text);

        var resolvedText = text.Replace("{callerId}", context.Caller.Number ?? "unknown");
        resolvedText = resolvedText.Replace("{calleeId}", context.Callee?.Number ?? "unknown");
        resolvedText = resolvedText.Replace("{callId}", context.CallId);
        resolvedText = resolvedText.Replace("{timestamp}", DateTime.UtcNow.ToString("o"));

        _logger.LogDebug("Resolved text: '{ResolvedText}'", resolvedText);

        return Task.FromResult(resolvedText);
    }
}