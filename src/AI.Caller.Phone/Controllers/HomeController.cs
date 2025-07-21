using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AI.Caller.Phone.Models;
using Microsoft.AspNetCore.Authorization;
using AI.Caller.Phone.Services;

namespace AI.Caller.Phone.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly ContactService _contactService;

        public HomeController(AppDbContext context, ContactService contactService)
        {
            _context = context;
            _contactService = contactService;
        }

        public async Task<IActionResult> Index()
        {
            var username = User.Identity.Name;
            var user = await _context.Users.Include(u => u.Contacts).FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                return NotFound();
            }

            return View(user.Contacts);
        }
    }
}