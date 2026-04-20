namespace AssetManagement.Models
{
    public class SelectionViewModel
    {
        public string? TableName { get; set; }
        public string? FilePath { get; set; }

        public List<DbColumnViewModel>? Columns { get; set; }
        public List<string>? SelectedColumns { get; set; }
    }
}
