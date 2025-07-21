using System;
using System.Security.Claims;

namespace AI.Caller.Phone {
    public static class ClaimsPrincipalExtension {
        public static T? FindFirst<T>(this ClaimsPrincipal claimsPrincipal, string claimType) {
            var value = claimsPrincipal.FindFirstValue(claimType);
            if (string.IsNullOrWhiteSpace(value)) {
                return default(T);
            }
            return (T)Convert.ChangeType(value, typeof(T));
        }
    }
}
