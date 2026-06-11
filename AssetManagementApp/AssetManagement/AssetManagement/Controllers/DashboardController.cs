using AssetManagement.Services;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    public class DashboardController : Controller
    {
        private readonly DashboardService _dService;
        private readonly DataService _db;
        public DashboardController(DashboardService dService, DataService db)
        {
            _dService = dService;
            _db = db;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpGet]
        public IActionResult GetDashboardMap()
        {
            var data = _dService.GetDashboardGeoJson();
            return Json(data);
        }
    }
}
