using AssetManagement.Adapter;
using Microsoft.AspNetCore.Mvc;
using AssetManagement.Models;
using AssetManagement.Services;

namespace AssetManagement.Controllers
{
    public class ImportController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public ImportController(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        // Upload Page
        public IActionResult Upload()
        {
            return View();
        }

        // Handle Upload
        [HttpPost]
        public async Task<IActionResult> Upload(DbUploadViewModel model)
        {
            if (model.DbFile == null || !model.DbFile.FileName.EndsWith(".db3"))
            {
                ModelState.AddModelError("", "Only .db3 files allowed");
                return View(model);
            }

            var uploads = Path.Combine(_env.WebRootPath, "uploads");
            Directory.CreateDirectory(uploads);

            var filePath = Path.Combine(uploads, model.DbFile.FileName);

            using var stream = new FileStream(filePath, FileMode.Create);
            await model.DbFile.CopyToAsync(stream);

            var adapter = new SQLiteAdapter(filePath);
            var tables = adapter.GetTables();

            ViewBag.FilePath = filePath;

            return View("Tables", tables);
        }

        // Select Table → Show Columns
        public IActionResult Columns(string tableName, string filePath)
        {
            var adapter = new SQLiteAdapter(filePath);
            var columns = adapter.GetColumns(tableName);

            var vm = new SelectionViewModel
            {
                TableName = tableName,
                FilePath = filePath,
                Columns = columns
            };

            return View(vm);
        }

        // Import Selected Data
        [HttpPost]
        public IActionResult Import(SelectionViewModel model)
        {
            var adapter = new SQLiteAdapter(model.FilePath);

            var data = adapter.GetTableData(model.TableName, model.SelectedColumns);

            var sqlService = new SQLService(
                _config.GetConnectionString("DefaultConnection"));

            sqlService.SaveTable(model.TableName, data);

            return View("Success");
        }
    }
}
