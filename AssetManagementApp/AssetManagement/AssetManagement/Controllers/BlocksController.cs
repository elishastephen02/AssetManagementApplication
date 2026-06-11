using Microsoft.AspNetCore.Mvc;
using AssetManagement.Services;

namespace AssetManagement.Controllers
{
    public class BlocksController : Controller
    {
        private readonly BlocksService _bService;

        public BlocksController(BlocksService bService)
        {
            _bService = bService;
        }

        //public IActionResult Import()
        //{
        //    var path = Path.Combine(
        //        Directory.GetCurrentDirectory(),
        //        "Data",
        //        "Westmead.geojson"
        //    );

        //    _bService.ImportBlocksFromGeoJson(path);

        //    return Content("Blocks imported successfully ✔");
        //}
    }
}