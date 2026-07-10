using AssetManagement.Models;
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
            var model = new DashboardViewModel
            {
                TotalLength = _db.QuerySingle<int>(
                    "SELECT SUM(CAST(LENGTH AS DECIMAL(10,2))) FROM GEOJSON"),

                TotalCR = _db.QuerySingle<decimal?>(
                    "SELECT AVG(CAST(ConditionR AS DECIMAL(10,2))) FROM GEOJSON"
                ) ?? 0,
                TotalCRC = _db.QuerySingle<decimal?>(
                    @"SELECT AVG(TRY_CAST(REPLACE(REPLACE(REPLACE(CurrentRC, 'R', ''),',', ''),' ', '')AS DECIMAL(18,2))) FROM GEOJSON"
                ) ?? 0
            };

            return View(model);
        }

        [HttpGet]
        public IActionResult GetDashboardMap()
        {
            var data = _dService.GetDashboardGeoJson();
            return Json(data);
        }
    }
}
