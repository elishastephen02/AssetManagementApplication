using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;

namespace AssetManagement.Services
{
    public class DataService
    {
        private readonly string? _connectionString;

        public DataService(IConfiguration config)
        {
            _connectionString = config.GetConnectionString("DefaultConnection");
        }

        public IEnumerable<dynamic> Query(string sql, object parameters = null)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            return db.Query(sql, parameters);
        }
    }
}
