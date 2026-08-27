using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    public class MapController : Controller
    {
        private readonly MapService _mapService;
        private readonly ManholeService _manholeService;

        public MapController(MapService mapService, ManholeService manholeService)
        {
            _mapService = mapService;
            _manholeService = manholeService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetMapData(double north, double south, double east, double west)
        {
            try
            {
                if (north <= south || east <= west)
                {
                    return BadRequest("Invalid bounding box: north/south/east/west are required and must form a valid box.");
                }

                var result = _mapService.GetMapData(north, south, east, west);
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
        public IActionResult Search(string term)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(term))
                    return Json(new List<object>());

                var results = _mapService.SearchPipes(term);

                return Json(results, new System.Text.Json.JsonSerializerOptions
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
        public IActionResult GetPipe(string id)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(id))
                    return BadRequest();

                var pipe = _mapService.GetPipe(id);

                if (pipe == null)
                    return NotFound();

                return Json(pipe, new System.Text.Json.JsonSerializerOptions
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
        public IActionResult GetManholeData()
        {
            var data = _manholeService.GetManholeData();
            return Json(data);
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

        //[HttpGet]
        //public IActionResult CheckMissingPictures()
        //{
        //    try
        //    {
        //        var missingIds = _mapService.CheckMissingPictures();

        //        return Json(new
        //        {
        //            Count = missingIds.Count,
        //            MissingOBJKeys = missingIds
        //        });
        //    }
        //    catch (Exception ex)
        //    {
        //        return StatusCode(500, ex.ToString());
        //    }
        //}
    }
}
