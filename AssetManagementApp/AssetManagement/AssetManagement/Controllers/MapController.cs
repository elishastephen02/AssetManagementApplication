using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    public class MapController : Controller
    {
        private readonly MapService _mapService;

        public MapController(MapService mapService)
        {
            _mapService = mapService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetMapData()
        {
            try
            {
                var result = _mapService.GetMapData();
                return Json(result, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNamingPolicy = null
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, ex.ToString());
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
                ".png"            => "image/png",
                ".gif"            => "image/gif",
                ".bmp"            => "image/bmp",
                ".webp"           => "image/webp",
                _                 => "application/octet-stream"
            };

            return PhysicalFile(imagePath, mimeType);
        }
    }
}