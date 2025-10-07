using System.Collections.Generic;
using System.Text;

namespace AI.Caller.Phone.Services;

public class VariableResolverService : IVariableResolverService {
    public string Resolve(string templateContent, Dictionary<string, string> variables) {
        if (string.IsNullOrEmpty(templateContent) || variables == null || variables.Count == 0) {
            return templateContent;
        }

        var resultBuilder = new StringBuilder(templateContent);

        foreach (var variable in variables) {
            string placeholder = $"{{{variable.Key}}}";
            resultBuilder.Replace(placeholder, variable.Value);
        }

        return resultBuilder.ToString();
    }
}