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

            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                geoJson = reader.ReadToEnd();
            }

            var result = _mapService.ProcessGeoJson(geoJson);

            return Json(result);
        }

        [HttpGet]
        public IActionResult GetSectionDetails(int id)
        {
            var result = _mapService.GetSectionDetails(id);

            return Json(result);
        }

        [HttpGet]
        public IActionResult Search(string query)
        {
            if (string.IsNullOrEmpty(query))
                return Json(new { });

            var result = _mapService.SearchInfrastructure(query);

            return Json(result);
        }
    }
}
