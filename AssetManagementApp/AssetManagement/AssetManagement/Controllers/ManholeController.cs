using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    public class ManholeController : Controller
    {
        private readonly ManholeService _manholeService;

        public ManholeController (ManholeService manholeService)
        {
            _manholeService = manholeService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetManholeMapData()
        {
            try
            {
                var data = _manholeService.GetManholeMapData();
                return Json(data);
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet]
        public IActionResult GetImage(string fileName)
        {
            var imagePath = Path.Combine(
                Directory.GetCurrentDirectory(), "wwwroot", "Picture", fileName);

            if (!System.IO.File.Exists(imagePath))
                return NotFound();

            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            var mimeType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".webp" => "image/webp",
                _ => "application/octet-stream"
            };

            return PhysicalFile(imagePath, mimeType);
        }
    }
}
