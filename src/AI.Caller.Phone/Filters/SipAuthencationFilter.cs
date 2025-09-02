using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Models;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AI.Caller.Phone.Filters {
    public class SipAuthencationFilter : IActionFilter {
        public void OnActionExecuted(ActionExecutedContext context) {
            
        }

        public void OnActionExecuting(ActionExecutingContext context) {
            if (context.HttpContext.User.Identity?.IsAuthenticated == true) {
                var applicationContext = context.HttpContext.RequestServices.GetRequiredService<ApplicationContext>();
                var dbContext = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();                 
                var userId = context.HttpContext.User!.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = dbContext.Users.Include(u => u.SipAccount).First(u => u.Id == userId);
                var sipService = context.HttpContext.RequestServices.GetRequiredService<SipService>();
                sipService.RegisterUserAsync(user);
            }
        }
    }
}
