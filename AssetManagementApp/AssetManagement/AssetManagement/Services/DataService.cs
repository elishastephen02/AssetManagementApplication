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
            return db.Query(sql, parameters).ToList();
        }

        public int Execute(string sql, object param = null)
        {
            using var conn = new SqlConnection(_connectionString);
            return conn.Execute(sql, param);
        }
        public T QuerySingle<T>(string sql, object param = null)
        {
            using IDbConnection db = new SqlConnection(_connectionString);
            return db.QuerySingle<T>(sql, param);
        }
    }
}