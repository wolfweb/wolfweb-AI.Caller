﻿using Microsoft.AspNetCore.Mvc;
using AI.Caller.Phone.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using AI.Caller.Phone.Services;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using AI.Caller.Phone.Entities;

namespace AI.Caller.Phone.Controllers {
    [AllowAnonymous]
    public class AccountController : Controller {
        private readonly ILogger _logger;
        private readonly AppDbContext _context;
        private readonly SipService _sipService;
        private readonly UserService _userService;
        private readonly ContactService _contactService;

        public AccountController(
            AppDbContext context,
            SipService sipService,
            UserService userService,
            ContactService contactService,
            ILogger<AccountController> logger) {
            _logger         = logger;
            _context        = context;
            _userService    = userService;
            _sipService     = sipService;
            _contactService = contactService;
        }

        [HttpGet]
        public IActionResult Register() {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(RegisterModel model) {
            ViewBag.IsAdmin = false;

            if (ModelState.IsValid) {
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == model.Username);
                if (existingUser != null) {
                    ModelState.AddModelError(string.Empty, "用户名已被使用");
                    return View(model);
                }

                var user = new User {
                    Username = model.Username,
                    Password = model.Password
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim("isAdmin", user.IsAdmin.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
                await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity));

                HttpContext.Session.SetInt32("UserId", (int)user.Id);
                HttpContext.Session.SetString("Username", user.Username);

                _logger.LogInformation($"用户 {user.Username} 注册成功并已登录");

                return RedirectToAction("Index", "Home");
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Login() {
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            ViewBag.IsAdmin = currentUser != null ? currentUser.IsAdmin : false;
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Login(LoginModel loginModel) {
            if (ModelState.IsValid) {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == loginModel.Username && u.Password == loginModel.Password);

                if (user != null) {
                    // Create claims for the user
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, user.Username),
                        new Claim("isAdmin", user.IsAdmin.ToString()),
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())
                    };

                    var claimsIdentity = new ClaimsIdentity(claims, "CookieAuth");
                    await HttpContext.SignInAsync("CookieAuth", new ClaimsPrincipal(claimsIdentity));

                    HttpContext.Session.SetInt32("UserId", (int)user.Id);
                    HttpContext.Session.SetString("Username", user.Username);

                    _logger.LogInformation($"用户 {user.Username} 登录成功");

                    return RedirectToAction("Index", "Home");
                }

                ModelState.AddModelError(string.Empty, "无效的用户名或密码");
            }

            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Username == User.Identity.Name);
            ViewBag.IsAdmin = currentUser != null ? currentUser.IsAdmin : false;
            return View(loginModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout() {
            await HttpContext.SignOutAsync("CookieAuth");
            HttpContext.Session.Clear();
            return RedirectToAction("Login");
        }

        [Authorize]
        public async Task<IActionResult> ManageContacts() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.Include(u => u.Contacts).FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) {
                return NotFound();
            }

            return View(user.Contacts ?? new List<Contact>());
        }

        [Authorize]
        public IActionResult AddContact() {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> AddContact(Contact contact) {
            if (ModelState.IsValid) {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user != null) {
                    contact.UserId = user.Id;
                    await _contactService.AddContactAsync(contact);
                    return RedirectToAction(nameof(ManageContacts));
                }
            }

            return View(contact);
        }

        [Authorize]
        public async Task<IActionResult> EditContact(int id) {
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null) {
                return NotFound();
            }

            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || contact.UserId != user.Id) {
                return Forbid();
            }

            return View(contact);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> EditContact(Contact contact) {
            if (ModelState.IsValid) {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null || contact.UserId != user.Id) {
                    return Forbid();
                }

                await _contactService.UpdateContactAsync(contact);
                return RedirectToAction(nameof(ManageContacts));
            }
            return View(contact);
        }

        [Authorize]
        public async Task<IActionResult> DeleteContact(int id) {
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null) {
                return NotFound();
            }

            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || contact.UserId != user.Id) {
                return Forbid();
            }

            return View(contact);
        }

        [HttpPost, ActionName("DeleteContact")]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> DeleteContactConfirmed(int id) {
            var contact = await _contactService.GetContactByIdAsync(id);
            if (contact == null) {
                return NotFound();
            }
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (user == null || contact.UserId != user.Id) {
                return Forbid();
            }

            await _contactService.DeleteContactAsync(id);
            return RedirectToAction(nameof(ManageContacts));
        }

        [Authorize]
        public async Task<IActionResult> ViewContacts() {
            return RedirectToAction(nameof(ManageContacts));
        }

        [Authorize]
        public async Task<IActionResult> SipSettings() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) {
                return NotFound();
            }

            var model = new SipSettingsModel {
                SipUsername = user.SipAccount?.SipUsername ?? "",
                SipPassword = user.SipAccount?.SipPassword ?? ""
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> SipSettings(SipSettingsModel model) {
            if (ModelState.IsValid) {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user != null) {
                    if (user.SipAccount != null) {
                        user.SipAccount.SipUsername = model.SipUsername;
                        user.SipAccount.SipPassword = model.SipPassword;
                    }
                    user.SipRegistered = false; // 重置注册状态，等待重新注册

                    await _context.SaveChangesAsync();

                    return RedirectToAction("SipSettings");
                }
            }

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> UserProfile() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

            if (user == null) {
                return NotFound();
            }

            var model = new UserProfileModel {
                Username = user.Username,
                Email = user.Email ?? string.Empty,
                PhoneNumber = user.PhoneNumber ?? string.Empty,
                DisplayName = user.DisplayName ?? string.Empty,
                Bio = user.Bio ?? string.Empty
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize]
        public async Task<IActionResult> UserProfile(UserProfileModel model) {
            if (ModelState.IsValid) {
                var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                if (user != null) {
                    user.Email = model.Email;
                    user.PhoneNumber = model.PhoneNumber;
                    user.DisplayName = model.DisplayName;
                    user.Bio = model.Bio;

                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "用户资料更新成功";
                } else {
                    TempData["ErrorMessage"] = "用户不存在";
                }

                return RedirectToAction(nameof(UserProfile));
            }

            return View(model);
        }

        [Authorize]
        public async Task<IActionResult> UserManagement() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                TempData["ErrorMessage"] = "您没有权限访问此页面";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.IsAdmin = true;
            var users = await _context.Users
                .Include(u => u.SipAccount)
                .ToListAsync();

            return View(users ?? new List<User>());
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddUser([FromBody] UserCreateModel model) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var user = new User {
                    Username = model.Username,
                    Password = model.Password, // 在实际应用中应该加密密码
                    DisplayName = model.DisplayName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "用户添加成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "添加用户时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditUser([FromBody] UserEditModel model) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == model.Id);
                if (user == null) {
                    return Json(new { success = false, message = "用户不存在" });
                }

                if (!string.IsNullOrEmpty(model.Password)) {
                    user.Password = model.Password; 
                }

                user.DisplayName = model.DisplayName;
                user.Email = model.Email;
                user.PhoneNumber = model.PhoneNumber;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "用户信息更新成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "更新用户信息时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteUser(int id) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
                if (user == null) {
                    return Json(new { success = false, message = "用户不存在" });
                }

                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "用户删除成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "删除用户时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AssignSipAccount([FromBody] AssignSipAccountModel model) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == model.UserId);
                if (user == null) {
                    return Json(new { success = false, message = "用户不存在" });
                }

                if (model.SipAccountId.HasValue) {
                    var sipAccount = await _context.SipAccounts.FirstOrDefaultAsync(s => s.Id == model.SipAccountId.Value);
                    if (sipAccount == null) {
                        return Json(new { success = false, message = "SIP账户不存在" });
                    }

                    user.SipAccountId = model.SipAccountId.Value;
                    user.SipAccount = sipAccount;
                } else {
                    user.SipAccountId = null;
                    user.SipAccount = null;
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "SIP账户分配成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "分配SIP账户时发生错误: " + ex.Message });
            }
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAvailableSipAccounts() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var sipAccounts = await _context.SipAccounts
                    .Where(s => s.IsActive)
                    .Select(s => new { s.Id, s.SipUsername })
                    .ToListAsync();

                return Json(sipAccounts);
            } catch (Exception ex) {
                return Json(new { success = false, message = "获取SIP账户列表时发生错误: " + ex.Message });
            }
        }

        [Authorize]
        public async Task<IActionResult> SipAccountManagement() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                TempData["ErrorMessage"] = "您没有权限访问此页面";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.IsAdmin = true;
            var sipAccounts = await _context.SipAccounts
                .Include(s => s.Users)
                .ToListAsync();

            return View(sipAccounts ?? new List<SipAccount>());
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AddSipAccount([FromBody] SipAccountCreateModel model) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var sipAccount = new SipAccount {
                    SipUsername = model.SipUsername,
                    SipPassword = model.SipPassword, // 在实际应用中应该加密密码
                    SipServer = model.SipServer,
                    IsActive = model.IsActive,
                    CreatedAt = DateTime.UtcNow
                };

                _context.SipAccounts.Add(sipAccount);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "SIP账户添加成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "添加SIP账户时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditSipAccount([FromBody] SipAccountEditModel model) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var sipAccount = await _context.SipAccounts.FirstOrDefaultAsync(s => s.Id == model.Id);
                if (sipAccount == null) {
                    return Json(new { success = false, message = "SIP账户不存在" });
                }

                if (!string.IsNullOrEmpty(model.SipPassword)) {
                    sipAccount.SipPassword = model.SipPassword; 
                }

                sipAccount.SipServer = model.SipServer;
                sipAccount.IsActive = model.IsActive;

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "SIP账户更新成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "更新SIP账户时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> DeleteSipAccount(int id) {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                return Json(new { success = false, message = "您没有权限执行此操作" });
            }

            try {
                var sipAccount = await _context.SipAccounts
                    .Include(s => s.Users)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (sipAccount == null) {
                    return Json(new { success = false, message = "SIP账户不存在" });
                }

                if (sipAccount.Users.Any()) {
                    return Json(new { success = false, message = "该SIP账户正在被用户使用，无法删除" });
                }

                _context.SipAccounts.Remove(sipAccount);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "SIP账户删除成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "删除SIP账户时发生错误: " + ex.Message });
            }
        }

        [Authorize]
        public async Task<IActionResult> ContactManagement() {
            var userId = User.FindFirst<int>(ClaimTypes.NameIdentifier);
            var currentUser = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (currentUser == null || !currentUser.IsAdmin) {
                TempData["ErrorMessage"] = "您没有权限访问此页面";
                return RedirectToAction("Index", "Home");
            }

            ViewBag.IsAdmin = true;
            var contacts = await _context.Contacts
                .Include(c => c.User)
                .ToListAsync();

            return View(contacts ?? new List<Contact>());
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAllUsers() {
            try {
                var users = await _context.Users
                    .Select(u => new { u.Id, u.Username })
                    .ToListAsync();

                return Json(users);
            } catch (Exception ex) {
                return Json(new { success = false, message = "获取用户列表时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> EditContact([FromBody] ContactEditModel model) {
            try {
                var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.Id == model.Id);
                if (contact == null) {
                    return Json(new { success = false, message = "联系人不存在" });
                }

                contact.Name = model.Name;
                contact.PhoneNumber = model.PhoneNumber;

                if (model.UserId > 0) {
                    contact.UserId = model.UserId;
                }

                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "联系人信息更新成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "更新联系人信息时发生错误: " + ex.Message });
            }
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> AdminDeleteContact(int id) {
            try {
                var contact = await _context.Contacts.FirstOrDefaultAsync(c => c.Id == id);
                if (contact == null) {
                    return Json(new { success = false, message = "联系人不存在" });
                }

                _context.Contacts.Remove(contact);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "联系人删除成功" });
            } catch (Exception ex) {
                return Json(new { success = false, message = "删除联系人时发生错误: " + ex.Message });
            }
        }
    }

    public class SipSettingsModel {
        public SipAccount? SipAccount { get; set; }
        public string? SipUsername { get; set; }
        public string? SipPassword { get; set; }
        public string? SipServer { get; set; }
    }

    public class UserProfileModel {
        public string Username { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DisplayName { get; set; }
        public string? Bio { get; set; }
    }

    public class UserCreateModel {
        public string Username { get; set; }
        public string Password { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DisplayName { get; set; }
    }

    public class UserEditModel {
        public int Id { get; set; }
        public string? Password { get; set; }
        public string? Email { get; set; }
        public string? PhoneNumber { get; set; }
        public string? DisplayName { get; set; }
    }

    public class AssignSipAccountModel {
        public int UserId { get; set; }
        public int? SipAccountId { get; set; }
    }

    public class SipAccountCreateModel {
        public string SipUsername { get; set; }
        public string SipPassword { get; set; }
        public string SipServer { get; set; }
        public bool IsActive { get; set; }
    }

    public class SipAccountEditModel {
        public int Id { get; set; }
        public string? SipPassword { get; set; }
        public string SipServer { get; set; }
        public bool IsActive { get; set; }
    }

    public class ContactEditModel {
        public int Id { get; set; }
        public string Name { get; set; }
        public string PhoneNumber { get; set; }
        public int UserId { get; set; }
    }
}