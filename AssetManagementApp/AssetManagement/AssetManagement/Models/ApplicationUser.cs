using Microsoft.AspNetCore.Identity;

namespace AssetManagement.Models
{
    public class ApplicationUser:IdentityUser
    {
        public bool IsApproved { get; set; } = false;
    }
}
