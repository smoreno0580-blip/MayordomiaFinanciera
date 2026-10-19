using Npgsql;
using System.Data;
using System.Globalization;

namespace MayordomiaFinanciera.Infrastructure.Repositories
{
    public class PostgresRepository
    {
        private readonly string _connectionString;

        public PostgresRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        private string ToPascalCase(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;
            var words = input.Split(new[] { '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                if (words[i].Length > 0)
                {
                    words[i] = char.ToUpper(words[i][0], CultureInfo.InvariantCulture) + words[i].Substring(1);
                }
            }
            return string.Join("", words);
        }

        // Método para consultas que devuelven datos (SELECT)
        public async Task<DataTable> ExecuteQueryAsync(string sqlQuery, NpgsqlParameter[]? parameters = null)
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                using (var command = new NpgsqlCommand(sqlQuery, connection))
                {
                    if (parameters != null) command.Parameters.AddRange(parameters);

                    var dt = new DataTable();
                    dt.Locale = CultureInfo.InvariantCulture;

                    await connection.OpenAsync();

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        var columnMappings = new List<(string TargetName, Type TargetType)>();
                        string queryLower = sqlQuery.ToLowerInvariant();

                        // 1. Definir columnas adaptando nombres y agregando alias contextuales inteligentes
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            string origName = reader.GetName(i);
                            string pascalName = ToPascalCase(origName);
                            Type fieldType = reader.GetFieldType(i);
                            Type targetType = (fieldType == typeof(DateOnly)) ? typeof(DateTime) : fieldType;

                            // Registrar nombre PascalCase principal
                            if (!dt.Columns.Contains(pascalName))
                            {
                                dt.Columns.Add(pascalName, targetType);
                            }
                            columnMappings.Add((pascalName, targetType));

                            // Registrar nombre original (snake_case) si es diferente
                            if (!string.Equals(origName, pascalName, StringComparison.OrdinalIgnoreCase) && !dt.Columns.Contains(origName))
                            {
                                dt.Columns.Add(origName, targetType);
                                columnMappings.Add((origName, targetType));
                            }

                            // Alias inteligentes automáticos si la columna es 'id' según la tabla consultada
                            if (string.Equals(origName, "id", StringComparison.OrdinalIgnoreCase))
                            {
                                if (queryLower.Contains("users") && !dt.Columns.Contains("UserId"))
                                {
                                    dt.Columns.Add("UserId", targetType);
                                    columnMappings.Add(("UserId", targetType));
                                }
                                if (queryLower.Contains("groups") && !dt.Columns.Contains("GroupId"))
                                {
                                    dt.Columns.Add("GroupId", targetType);
                                    columnMappings.Add(("GroupId", targetType));
                                }
                                if (queryLower.Contains("sessions") && !dt.Columns.Contains("SessionId"))
                                {
                                    dt.Columns.Add("SessionId", targetType);
                                    columnMappings.Add(("SessionId", targetType));
                                }
                            }
                        }

                        // 2. Cargar los datos rellenando todas las variantes de nombres por cada fila
                        dt.BeginLoadData();
                        while (await reader.ReadAsync())
                        {
                            var row = dt.NewRow();

                            // Leer los valores originales del reader en orden
                            object[] rawValues = new object[reader.FieldCount];
                            reader.GetValues(rawValues);

                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                object val = rawValues[i];
                                if (val is DateOnly dOnly)
                                {
                                    val = dOnly.ToDateTime(TimeOnly.MinValue);
                                }
                                else if (val == DBNull.Value)
                                {
                                    val = DBNull.Value;
                                }

                                string origName = reader.GetName(i);
                                string pascalName = ToPascalCase(origName);

                                // Asignar a PascalCase y snake_case
                                if (dt.Columns.Contains(pascalName)) row[pascalName] = val;
                                if (dt.Columns.Contains(origName)) row[origName] = val;

                                // Asignar a alias contextuales si aplican
                                if (string.Equals(origName, "id", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (queryLower.Contains("users") && dt.Columns.Contains("UserId")) row["UserId"] = val;
                                    if (queryLower.Contains("groups") && dt.Columns.Contains("GroupId")) row["GroupId"] = val;
                                    if (queryLower.Contains("sessions") && dt.Columns.Contains("SessionId")) row["SessionId"] = val;
                                }
                            }
                            dt.Rows.Add(row);
                        }
                        dt.EndLoadData();
                    }
                    return dt;
                }
            }
        }

        // Método para operaciones de modificación (INSERT, UPDATE, DELETE)
        public async Task<int> ExecuteNonQueryAsync(string sqlQuery, NpgsqlParameter[]? parameters = null)
        {
            using (var connection = new NpgsqlConnection(_connectionString))
            {
                using (var command = new NpgsqlCommand(sqlQuery, connection))
                {
                    if (parameters != null) command.Parameters.AddRange(parameters);

                    await connection.OpenAsync();
                    return await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}