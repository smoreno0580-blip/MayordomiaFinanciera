using Microsoft.Data.SqlClient;
using System.Data;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Infrastructure.Repositories
{
    public class SqlRepository
    {
        private readonly string _connectionString;

        // Recibe directamente el string de conexión
        public SqlRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<DataTable> ExecuteStoredProcedureAsync(string spName, SqlParameter[] parameters)
        {
            DataTable dataTable = new DataTable();
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(spName, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    using (var adapter = new SqlDataAdapter(command))
                    {
                        await Task.Run(() => adapter.Fill(dataTable));
                    }
                }
            }
            return dataTable;
        }

        public async Task<int> ExecuteNonQueryAsync(string spName, SqlParameter[] parameters)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                using (var command = new SqlCommand(spName, connection))
                {
                    command.CommandType = CommandType.StoredProcedure;
                    if (parameters != null)
                    {
                        command.Parameters.AddRange(parameters);
                    }
                    await connection.OpenAsync();
                    return await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}