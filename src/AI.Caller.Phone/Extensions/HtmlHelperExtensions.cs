using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace AI.Caller.Phone;

public static class HtmlHelperExtensions {
    public static IEnumerable<SelectListItem> GetEnumStringValueSelectList<TEnum>(this IHtmlHelper htmlHelper)
        where TEnum : struct, Enum {
        return Enum.GetValues<TEnum>()
            .Select(e => new SelectListItem {
                Value = e.ToString(),                    
                Text = e.GetDisplayName() ?? e.ToString()
            });
    }

    private static string? GetDisplayName(this Enum value) {
        return value.GetType()
            .GetMember(value.ToString())[0]
            .GetCustomAttribute<DisplayAttribute>()
            ?.GetName();
    }
}