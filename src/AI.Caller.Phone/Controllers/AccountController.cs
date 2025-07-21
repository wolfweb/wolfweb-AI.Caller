using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using AI.Caller.Phone.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Controllers
{
    [AllowAnonymous]
    public class AccountController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserService _userService;
        private readonly ContactService _contactService;
        private readonly SipService _sipService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            AppDbContext context, 
            UserService userService, 
            ContactService contactService,
            SipService sipService,
            ILogger<AccountController> logger)
        {
            _context = context;
            _userService = userService;
            _contactService = contactService;
            _sipService = sipService;
            _logger = logger;
        }

        [HttpGet]
        public IActionResult Register()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterModel model)
        {
            if (ModelState.IsValid)
            {
                // 检查用户名是否已存在
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == model.Username);
                if (existingUser != null)
                {
                    ModelState.AddModelError(string.Empty, "用户名已被使用");
                    return View(model);
                }

                // 创建新用户
                var user = new User
                {
                    Username = model.Username,
                    Password = model.Password
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // 自动登录新注册的用户
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("SipUser", user.SipUsername),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
                await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity));

                // 设置会话
                HttpContext.Session.SetInt32("UserId", (int)user.Id);
                HttpContext.Session.SetString("SipUser", user.SipUsername);
                HttpContext.Session.SetString("Username", user.Username);

                _logger.LogInformation($"用户 {user.Username} 注册成功并已登录");

                return RedirectToAction("Index", "Home");
            }

            return View(model);
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginModel loginModel)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == loginModel.Username && u.Password == loginModel.Password);

                if (user != null)
                {
                    // Create claims for the user
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim("SipUser", user.SipUsername),
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
                    var authProperties = new AuthenticationProperties
                    {
                        IsPersistent = loginModel.RememberMe
                    };

                    await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity), authProperties);

                    HttpContext.Session.SetInt32("UserId", (int)user.Id);
                    HttpContext.Session.SetString("SipUser", user.SipUsername);
                    HttpContext.Session.SetString("Username", user.Username);

                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            }

            return View(loginModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync("CookieAuth");
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [Authorize]
        public async Task<IActionResult> ManageContacts()
        {
            var username = User.Identity.Name;
            var user = await _context.Users.Include(u => u.Contacts).FirstOrDefaultAsync(u => u.Username == username);

            if (user == null)
            {
                return NotFound();
            }

            return View(user.Contacts);
        }

        [Authorize]
        public IActionResult AddContact()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> AddContact(Contact contact)
        {
            if (ModelState.IsValid)
            {
                // Get the current user from the database
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user != null)
                {
                    contact.UserId = user.Id;
                    await _contactService.AddContactAsync(contact);
                    return RedirectToAction(nameof(ManageContacts));
                }
            }

            return View(contact);
        }

        [Authorize]
        public async Task<IActionResult> EditContact(int id)
        {
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null)
            {
                return NotFound();
            }

            // Verify that the contact belongs to the current user
            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || contact.UserId != user.Id)
            {
                return Forbid();
            }

            return View(contact);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> EditContact(Contact contact)
        {
            if (ModelState.IsValid)
            {
                // Verify that the contact belongs to the current user
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
                if (user == null || contact.UserId != user.Id)
                {
                    return Forbid();
                }

                await _contactService.UpdateContactAsync(contact);
                return RedirectToAction(nameof(ManageContacts));
            }
            return View(contact);
        }

        [Authorize]
        public async Task<IActionResult> DeleteContact(int id)
        {
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null)
            {
                return NotFound();
            }

            // Verify that the contact belongs to the current user
            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || contact.UserId != user.Id)
            {
                return Forbid();
            }

            return View(contact);
        }

        [HttpPost, ActionName("DeleteContact")]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> DeleteContactConfirmed(int id)
        {
            // Verify that the contact belongs to the current user
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null)
            {
                return NotFound();
            }

            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            if (user == null || contact.UserId != user.Id)
            {
                return Forbid();
            }

            await _contactService.DeleteContactAsync(id);
            return RedirectToAction(nameof(ManageContacts));
        }

        [Authorize]
        public async Task<IActionResult> ViewContacts()
        {
            return RedirectToAction(nameof(ManageContacts));
        }

        [Authorize]
        public async Task<IActionResult> SipSettings()
        {
            var username = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);
            
            if (user == null)
            {
                return NotFound();
            }
            
            var model = new SipSettingsModel
            {
                SipUsername = user.SipUsername,
                SipPassword = user.SipPassword
            };
            
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> SipSettings(SipSettingsModel model)
        {
            if (ModelState.IsValid)
            {
                var username = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username);

                if (user != null)
                {
                    user.SipUsername = model.SipUsername;
                    user.SipPassword = model.SipPassword;
                    user.SipRegistered = false; // 重置注册状态，等待重新注册

                    await _context.SaveChangesAsync();

                    return RedirectToAction("SipSettings");
                }
            }

            return View(model);
        }
    }

    public class SipSettingsModel
    {
        public string? SipUsername { get; set; }
        public string? SipPassword { get; set; }
        public string? SipServer { get; set; }
    }
}