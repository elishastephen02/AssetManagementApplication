using Microsoft.Data.SqlClient;
using System.Data;

namespace AssetManagement.Services
{
    public class SQLService
    {
        private readonly string _connectionString;

        public SQLService(string connectionString)
        {
            _connectionString = connectionString;
        }

        public void SaveTable(string tableName, DataTable data)
        {
            using var conn = new SqlConnection(_connectionString);
            conn.Open();

            // Drop table if exists
            var dropCmd = new SqlCommand(
                $"IF OBJECT_ID('{tableName}', 'U') IS NOT NULL DROP TABLE [{tableName}]",
                conn);
            dropCmd.ExecuteNonQuery();

            // Create table
            var columnsSql = string.Join(", ",
                data.Columns.Cast<DataColumn>()
                .Select(c => $"[{c.ColumnName}] NVARCHAR(MAX)")
            );

            var createCmd = new SqlCommand(
                $"CREATE TABLE [{tableName}] ({columnsSql})",
                conn);

            createCmd.ExecuteNonQuery();

            // Insert data
            foreach (DataRow row in data.Rows)
            {
                var colNames = string.Join(",", data.Columns.Cast<DataColumn>().Select(c => $"[{c.ColumnName}]"));
                var paramNames = string.Join(",", data.Columns.Cast<DataColumn>().Select(c => $"@{c.ColumnName}"));

                var insertCmd = new SqlCommand(
                    $"INSERT INTO [{tableName}] ({colNames}) VALUES ({paramNames})",
                    conn);

                foreach (DataColumn col in data.Columns)
                {
                    insertCmd.Parameters.AddWithValue("@" + col.ColumnName, row[col] ?? DBNull.Value);
                }

                insertCmd.ExecuteNonQuery();
            }
        }
    }
}
