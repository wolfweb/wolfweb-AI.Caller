using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Mvc.Filters;

namespace AI.Caller.Phone.Filters {
    public class SipAuthencationFilter : IActionFilter {
        public void OnActionExecuted(ActionExecutedContext context) {
            
        }

        public void OnActionExecuting(ActionExecutingContext context) {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true) {
                var applicationContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationContext>();
                var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>(); 
                var name = context.HttpContext.User.Identity.Name;
                var user = dbContext.Users.First(u => u.Username == name);
                var sipService = context.HttpContext.RequestServices.GetRequiredService<SipService>();
                sipService.RegisterUserAsync(user);
            }
        }
    }
}
