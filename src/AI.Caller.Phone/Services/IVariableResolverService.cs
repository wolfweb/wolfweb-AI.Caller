using System.Collections.Generic;

namespace AI.Caller.Phone.Services;

public interface IVariableResolverService {
    string Resolve(string templateContent, Dictionary<string, string> variables);
}