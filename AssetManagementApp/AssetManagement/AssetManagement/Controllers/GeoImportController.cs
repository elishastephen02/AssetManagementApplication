using AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace AssetManagement.Controllers
{
    [Authorize(Roles = "PrimaryUser")]
    public class GeoImportController : Controller
    {
        private readonly ImportService _importService;
        private readonly ManholeImportService _manholeImport;

        public GeoImportController(ImportService importService, ManholeImportService manholeImport)
        {
            _importService = importService;
            _manholeImport = manholeImport;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public IActionResult Upload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file selected.");

            string geoJson;

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                geoJson = reader.ReadToEnd();
            }

            geoJson = geoJson.Trim().TrimStart('\uFEFF', '\u200B');

            if (!geoJson.StartsWith("{"))
                return BadRequest("Invalid GeoJSON file.");

            var result = _importService.ImportGeoJson(geoJson);

            return Ok(result);
        }

        [HttpGet]
        public IActionResult MUpload()
        {
            return View();
        }

        [HttpPost]
        public IActionResult MUpload(IFormFile file)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file selected.");

            string geoJson;

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                geoJson = reader.ReadToEnd();
            }

            geoJson = geoJson.Trim().TrimStart('\uFEFF', '\u200B');

            if (!geoJson.StartsWith("{"))
                return BadRequest("Invalid GeoJSON file.");

            var result = _manholeImport.ImportGeoJson(geoJson);

            return Ok(result);
        }
    }
}