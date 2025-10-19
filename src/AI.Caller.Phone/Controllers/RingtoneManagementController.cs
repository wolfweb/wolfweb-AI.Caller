using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Caller.Phone.Controllers;

[Authorize]
public class RingtoneManagementController : Controller {
    public IActionResult Index() {
        return View();
    }
}