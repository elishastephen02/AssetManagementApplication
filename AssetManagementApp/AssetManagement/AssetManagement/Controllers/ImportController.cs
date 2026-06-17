using AssetManagement.Adapter;
using AssetManagement.Models;
using AssetManagement.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AssetManagement.Controllers
{
    [Authorize(Roles = "PrimaryUser")]
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
        [HttpPost]
        public IActionResult Columns(List<string> selectedTables, string filePath)
        {
            if (selectedTables == null || !selectedTables.Any())
            {
                ModelState.AddModelError("", "Select at least one table.");
                return RedirectToAction("Upload");
            }

            var adapter = new SQLiteAdapter(filePath);

            var vm = new MultiSelectionViewModel
            {
                FilePath = filePath,
                Tables = selectedTables.Select(t => new SelectionViewModel
                {
                    TableName = t,
                    Columns = adapter.GetColumns(t)
                }).ToList()
            };

            return View(vm);
        }

        // Step 3: import each table's selected columns
        [HttpPost]
        public IActionResult Import(MultiSelectionViewModel model)
        {
            var adapter = new SQLiteAdapter(model.FilePath);
            var sqlService = new SQLService(_config.GetConnectionString("DefaultConnection"));

            foreach (var table in model.Tables)
            {
                if (table.SelectedColumns == null || !table.SelectedColumns.Any())
                    continue; // skipped table — nothing to import

                var data = adapter.GetTableData(table.TableName, table.SelectedColumns);
                sqlService.SaveTable(table.TableName, data);
            }

            return View("Success");
        }
    }
}
