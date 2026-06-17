namespace AssetManagement.Models
{
    public class SelectionViewModel
    {
        public string TableName { get; set; } = "";
        public List<DbColumnViewModel> Columns { get; set; } = new();
        public List<string> SelectedColumns { get; set; } = new();
    }

    public class MultiSelectionViewModel
    {
        public string FilePath { get; set; } = "";
        public List<SelectionViewModel> Tables { get; set; } = new();
    }
}
