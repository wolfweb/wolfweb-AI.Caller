using AI.Caller.Phone.Entities;
using AI.Caller.Phone.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Security.Claims;
using AI.Caller.Phone.Models;

namespace AI.Caller.Phone.Controllers {
    [Authorize]
    public class HomeController : Controller {
        private readonly ILogger<HomeController> _logger;
        private readonly AppDbContext _context;

        public HomeController(ILogger<HomeController> logger, AppDbContext context) {
            _logger = logger;
            _context = context;
        }

        public async Task<IActionResult> Index() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            
            var contacts = currentUser != null 
                ? await _context.Contacts.Where(c => c.UserId == currentUser.Id).ToListAsync()
                : new List<Contact>();
            
            return View(contacts);
        }

        public async Task<IActionResult> About() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            ViewBag.IsAdmin = currentUser != null ? currentUser.IsAdmin : false;
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public async Task<IActionResult> Error() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            ViewBag.IsAdmin = currentUser != null ? currentUser.IsAdmin : false;
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}