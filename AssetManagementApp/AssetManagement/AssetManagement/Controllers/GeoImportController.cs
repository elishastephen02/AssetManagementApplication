using AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.IO;
using System.Text.RegularExpressions;

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
        [RequestSizeLimit(104857600)]
        [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
        public IActionResult Upload(IFormFile file, string? blockName)
        {
            Console.WriteLine($"File is null: {file == null}");

            if (file != null)
            {
                Console.WriteLine($"File name: {file.FileName}");
                Console.WriteLine($"File size: {file.Length}");
            }

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

            if (string.IsNullOrWhiteSpace(blockName))
            {
                var fileName = Path.GetFileNameWithoutExtension(file.FileName);

                var match = Regex.Match(fileName, @"^[^_]+_([^_]+)_Pipes$", RegexOptions.IgnoreCase);

                if (match.Success)
                {
                    blockName = match.Groups[1].Value;
                }
                else
                {
                    blockName = fileName;
                }
            }

            var result = _importService.ImportGeoJson(geoJson, blockName);

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