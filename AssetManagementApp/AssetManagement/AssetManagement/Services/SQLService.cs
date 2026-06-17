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

            // Get existing SQL table columns
            var existingColumns = new List<string>();

            using (var cmd = new SqlCommand(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = @TableName", conn))
            {
                cmd.Parameters.AddWithValue("@TableName", tableName);

                using var reader = cmd.ExecuteReader();

                while (reader.Read())
                {
                    existingColumns.Add(reader.GetString(0));
                }
            }

            // Keep only columns that exist in SQL Server
            var validColumns = data.Columns.Cast<DataColumn>()
                .Where(c => existingColumns.Contains(c.ColumnName))
                .ToList();

            if (!validColumns.Any())
            {
                throw new Exception($"No matching columns found in table {tableName}");
            }

            var colNames = string.Join(",",
                validColumns.Select(c => $"[{c.ColumnName}]"));

            var paramNames = string.Join(",",
                validColumns.Select(c => $"@{c.ColumnName}"));

            var sql = $@"
                INSERT INTO [{tableName}]
                ({colNames})
                VALUES
                ({paramNames})";

            // Start a transaction covering all rows for this table
            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (DataRow row in data.Rows)
                {
                    using var insertCmd = new SqlCommand(sql, conn, transaction);

                    foreach (var col in validColumns)
                    {
                        insertCmd.Parameters.AddWithValue(
                            "@" + col.ColumnName,
                            row[col.ColumnName] ?? DBNull.Value);
                    }

                    insertCmd.ExecuteNonQuery();
                }

                // All rows inserted successfully — make it permanent
                transaction.Commit();
            }
            catch
            {
                // Something failed partway through — undo every insert in this batch
                transaction.Rollback();
                throw;
            }
        }

        //public void SaveTable(string tableName, DataTable data)
        //{
        //    using var conn = new SqlConnection(_connectionString);
        //    conn.Open();

        //    // Get existing SQL table columns
        //    var existingColumns = new List<string>();

        //    using (var cmd = new SqlCommand(@"
        //        SELECT COLUMN_NAME
        //        FROM INFORMATION_SCHEMA.COLUMNS
        //        WHERE TABLE_NAME = @TableName", conn))
        //    {
        //        cmd.Parameters.AddWithValue("@TableName", tableName);

        //        using var reader = cmd.ExecuteReader();
        //        while (reader.Read())
        //        {
        //            existingColumns.Add(reader.GetString(0));
        //        }
        //    }

        //    var validColumns = data.Columns.Cast<DataColumn>()
        //        .Where(c => existingColumns.Contains(c.ColumnName))
        //        .ToList();

        //    if (!validColumns.Any())
        //        throw new Exception($"No matching columns found in table {tableName}");

        //    foreach (DataRow row in data.Rows)
        //    {
        //        var keyColumn = "STA_PK"; // your primary key

        //        var nonKeyColumns = validColumns
        //            .Where(c => c.ColumnName != keyColumn)
        //            .ToList();

        //        var setClause = string.Join(",",
        //            nonKeyColumns.Select(c => $"[{c.ColumnName}] = @{c.ColumnName}"));

        //        var colNames = string.Join(",",
        //            validColumns.Select(c => $"[{c.ColumnName}]"));

        //        var paramNames = string.Join(",",
        //            validColumns.Select(c => $"@{c.ColumnName}"));

        //        var sql = $@"
        //            IF EXISTS (SELECT 1 FROM [{tableName}] WHERE [{keyColumn}] = @STA_PK)
        //            BEGIN
        //                UPDATE [{tableName}]
        //                SET {setClause}
        //                WHERE [{keyColumn}] = @STA_PK
        //            END
        //            ELSE
        //            BEGIN
        //                INSERT INTO [{tableName}] ({colNames})
        //                VALUES ({paramNames})
        //            END";

        //        using var cmd = new SqlCommand(sql, conn);

        //        foreach (var col in validColumns)
        //        {
        //            cmd.Parameters.AddWithValue(
        //                "@" + col.ColumnName,
        //                row[col.ColumnName] ?? DBNull.Value);
        //        }

        //        cmd.ExecuteNonQuery();
        //    }
        //}
    }
}