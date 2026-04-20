using Microsoft.Data.Sqlite;
using System.Data;
using AssetManagement.Models;

namespace AssetManagement.Adapter
{
    public class SQLiteAdapter
    {
        private readonly string _connectionString;

        public SQLiteAdapter(string filePath)
        {
            _connectionString = $"Data Source={filePath}";
        }

        // Get Tables
        public List<string> GetTables()
        {
            var tables = new List<string>();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }

        // Get Columns
        public List<DbColumnViewModel> GetColumns(string tableName)
        {
            var columns = new List<DbColumnViewModel>();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info([{tableName}]);";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                columns.Add(new DbColumnViewModel
                {
                    ColumnName = reader["name"].ToString(),
                    DataType = reader["type"].ToString()
                });
            }

            return columns;
        }

        // Get Data
        public DataTable GetTableData(string tableName, List<string> selectedColumns)
        {
            var dt = new DataTable();

            using var conn = new SqliteConnection(_connectionString);
            conn.Open();

            string cols = string.Join(",", selectedColumns.Select(c => $"[{c}]"));

            var cmd = conn.CreateCommand();
            cmd.CommandText = $"SELECT {cols} FROM [{tableName}]";

            using var reader = cmd.ExecuteReader();
            dt.Load(reader);

            return dt;
        }
    }
}
