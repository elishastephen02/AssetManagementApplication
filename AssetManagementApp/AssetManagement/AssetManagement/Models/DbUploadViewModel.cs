using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace AssetManagement.Models
{
    public class DbUploadViewModel
    {
        [Required]
        public IFormFile? DbFile { get; set; }
    }
}
