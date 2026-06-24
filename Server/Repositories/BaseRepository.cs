using Microsoft.Data.SqlClient;
using Npgsql;
using System.Data;

namespace Server.Repositories
{

    public abstract class BaseRepository
    {
        protected readonly string _connectionString;
        protected readonly string _provider;

        protected BaseRepository(string connectionString, string provider = "PostgreSql")
        {
            _connectionString = connectionString;
            _provider = provider;
        }

        protected IDbConnection CreateConnection()
        {
            if (_provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
            {
                return new SqlConnection(_connectionString);
            }
            return new NpgsqlConnection(_connectionString);
        }
	
        protected IDbTransaction BeginTransaction(IDbConnection connection)
        {
            if (connection is IDbTransaction transaction)
                return transaction;

            if (connection is NpgsqlConnection npgsqlConn)
                return npgsqlConn.BeginTransaction();
            
            if (connection is SqlConnection sqlConn)
                return sqlConn.BeginTransaction();
            
            throw new InvalidOperationException($"Unsupported provider '{_provider}' for transaction management.");
        }
    }
}
