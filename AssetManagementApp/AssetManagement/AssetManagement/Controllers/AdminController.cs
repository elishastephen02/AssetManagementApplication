using AssetManagement.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    [Authorize(Roles = "PrimaryUser")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        public IActionResult PendingUsers()
        {
            var users = _userManager.Users
                .Where(u => !u.IsApproved)
                .ToList();

            return View(users);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id)
        {
            var user = await _userManager.FindByIdAsync(id);

            if (user != null)
            {
                user.IsApproved = true;
                await _userManager.UpdateAsync(user);
            }

            return RedirectToAction(nameof(PendingUsers));
        }
    }

}
