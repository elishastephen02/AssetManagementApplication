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

        [HttpPost]
        public IActionResult UploadGeoJson(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded");

            string geoJson;
            using (var reader = new StreamReader(file.OpenReadStream(),
                   System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                geoJson = reader.ReadToEnd();

            // Trim any leading whitespace or BOM characters
            geoJson = geoJson.Trim().TrimStart('\uFEFF', '\u200B');

            if (!geoJson.StartsWith("{"))
                return BadRequest($"Invalid GeoJSON - content starts with: {geoJson.Substring(0, Math.Min(50, geoJson.Length))}");

            var result = _mapService.ProcessGeoJson(geoJson);

            return Json(result, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = null
            });
        }

        [HttpGet]
        public IActionResult GetSectionDetails(int id)
        {
            var result = _mapService.GetSectionDetails(id);

            return Json(result);
        }

        [HttpGet]
        public IActionResult GetImage(string fileName)
        {
            // Adjust this path to wherever images are stored on disk
            var imagePath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "Picture", fileName);

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
