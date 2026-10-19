using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Text;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize(Roles = "admin")]
    public class UsersController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public UsersController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // Consultas directas adaptadas a PostgreSQL con JOIN al nombre del grupo
            string usersQuery = @"SELECT u.*, g.name AS group_name 
                                  FROM users u 
                                  LEFT JOIN groups g ON u.group_id = g.id 
                                  ORDER BY u.created_at DESC";

            DataTable dtUsers = await _postgresRepository.ExecuteQueryAsync(usersQuery, null);
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups WHERE is_active = true ORDER BY name", null);

            ViewBag.GroupsList = dtGroups;
            return View(dtUsers);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertUser(Guid? id, string name, string email, string phone, string role, string country, DateTime? birthDate, Guid? groupId, string passwordHash)
        {
            string query = "";
            NpgsqlParameter[] parameters;

            if (id.HasValue && id.Value != Guid.Empty)
            {
                query = @"UPDATE users 
                          SET name = @Name, email = @Email, phone = @Phone, role = @Role, 
                              country = @Country, birth_date = @BirthDate, group_id = @GroupId, 
                              password_hash = COALESCE(NULLIF(@PasswordHash, ''), password_hash)
                          WHERE id = @Id";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@Name", name),
                    new NpgsqlParameter("@Email", email),
                    new NpgsqlParameter("@Phone", (object?)phone ?? DBNull.Value),
                    new NpgsqlParameter("@Role", role ?? "student"),
                    new NpgsqlParameter("@Country", country ?? "Panamá"),
                    new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                    new NpgsqlParameter("@GroupId", (object?)groupId ?? DBNull.Value),
                    new NpgsqlParameter("@PasswordHash", (object?)passwordHash ?? DBNull.Value)
                };
            }
            else
            {
                query = @"INSERT INTO users (name, email, phone, role, country, birth_date, group_id, password_hash, created_at) 
                          VALUES (@Name, @Email, @Phone, @Role, @Country, @BirthDate, @GroupId, @PasswordHash, NOW())";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Name", name),
                    new NpgsqlParameter("@Email", email),
                    new NpgsqlParameter("@Phone", (object?)phone ?? DBNull.Value),
                    new NpgsqlParameter("@Role", role ?? "student"),
                    new NpgsqlParameter("@Country", country ?? "Panamá"),
                    new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                    new NpgsqlParameter("@GroupId", (object?)groupId ?? DBNull.Value),
                    new NpgsqlParameter("@PasswordHash", passwordHash ?? "123456")
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Id", id) };
            await _postgresRepository.ExecuteNonQueryAsync("DELETE FROM users WHERE id = @Id", parameters);
            return RedirectToAction("Index");
        }

        // 1. Exportar Usuarios a CSV
        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            string query = @"SELECT u.*, g.name AS group_name 
                              FROM users u 
                              LEFT JOIN groups g ON u.group_id = g.id 
                              ORDER BY u.created_at DESC";

            DataTable dtUsers = await _postgresRepository.ExecuteQueryAsync(query, null);
            var sb = new StringBuilder();

            // BOM para que Excel reconozca tildes y caracteres latinos correctamente
            sb.Append('\ufeff');
            sb.AppendLine("Nombre,Correo,Telefono,País,Cumpleaños,Rol,Grupo");

            foreach (DataRow row in dtUsers.Rows)
            {
                string name = row["name"]?.ToString()?.Replace("\"", "\"\"") ?? "";
                string email = row["email"]?.ToString()?.Replace("\"", "\"\"") ?? "";
                string phone = row["phone"]?.ToString()?.Replace("\"", "\"\"") ?? "";
                string country = row["country"]?.ToString()?.Replace("\"", "\"\"") ?? "Panamá";
                string birthDate = row["birth_date"] != DBNull.Value ? Convert.ToDateTime(row["birth_date"]).ToString("yyyy-MM-dd") : "";
                string role = row["role"]?.ToString() ?? "student";
                string groupName = row["group_name"]?.ToString()?.Replace("\"", "\"\"") ?? "Ninguno";

                sb.AppendLine($"\"{name}\",\"{email}\",\"{phone}\",\"{country}\",\"{birthDate}\",\"{role}\",\"{groupName}\"");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());
            string fileName = $"Usuarios_Compass_{DateTime.Now:yyyy-MM-dd}.csv";
            return File(buffer, "text/csv", fileName);
        }

        // 2. Importación Masiva de Usuarios vía Texto / CSV
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportUsers(string rawData, Guid? defaultGroupId)
        {
            if (string.IsNullOrWhiteSpace(rawData))
            {
                TempData["Error"] = "Por favor ingrese o cargue datos válidos para importar.";
                return RedirectToAction("Index");
            }

            var lines = rawData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            int successCount = 0;
            int errorCount = 0;

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.ToLower().StartsWith("nombre")) continue;

                var parts = trimmedLine.Contains('\t') ? trimmedLine.Split('\t') : trimmedLine.Split(',');
                if (parts.Length < 2)
                {
                    errorCount++;
                    continue;
                }

                string name = parts[0].Replace("\"", "").Trim();
                string email = parts[1].Replace("\"", "").Trim();
                string phone = parts.Length > 2 ? parts[2].Replace("\"", "").Trim() : "";
                string password = parts.Length > 3 && !string.IsNullOrEmpty(parts[3].Replace("\"", "").Trim()) ? parts[3].Replace("\"", "").Trim() : "compass123";
                string country = parts.Length > 4 && !string.IsNullOrEmpty(parts[4].Replace("\"", "").Trim()) ? parts[4].Replace("\"", "").Trim() : "Panamá";
                string birthDateStr = parts.Length > 5 ? parts[5].Replace("\"", "").Trim() : "";
                string role = parts.Length > 6 ? parts[6].Replace("\"", "").Trim().ToLower() : "student";

                if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(name))
                {
                    errorCount++;
                    continue;
                }

                try
                {
                    DateTime? birthDate = DateTime.TryParse(birthDateStr, out var parsedDate) ? parsedDate : null;

                    string importQuery = @"INSERT INTO users (name, email, phone, password_hash, role, country, birth_date, group_id, created_at) 
                                           VALUES (@Name, @Email, @Phone, @PasswordHash, @Role, @Country, @BirthDate, @GroupId, NOW())
                                           ON CONFLICT (email) DO UPDATE 
                                           SET name = EXCLUDED.name, phone = EXCLUDED.phone, role = EXCLUDED.role, 
                                               country = EXCLUDED.country, birth_date = EXCLUDED.birth_date, group_id = EXCLUDED.group_id";

                    var parameters = new NpgsqlParameter[]
                    {
                        new NpgsqlParameter("@Name", name),
                        new NpgsqlParameter("@Email", email),
                        new NpgsqlParameter("@Phone", (object?)phone ?? DBNull.Value),
                        new NpgsqlParameter("@PasswordHash", password),
                        new NpgsqlParameter("@Role", role),
                        new NpgsqlParameter("@Country", country),
                        new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                        new NpgsqlParameter("@GroupId", (object?)defaultGroupId ?? DBNull.Value)
                    };

                    await _postgresRepository.ExecuteNonQueryAsync(importQuery, parameters);
                    successCount++;
                }
                catch
                {
                    errorCount++;
                }
            }

            TempData["Success"] = $"Importación finalizada: {successCount} usuarios procesados con éxito, {errorCount} errores.";
            return RedirectToAction("Index");
        }
    }
}