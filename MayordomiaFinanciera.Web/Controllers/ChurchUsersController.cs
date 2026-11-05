using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class ChurchUsersController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public ChurchUsersController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        private async Task<(Guid churchId, string userChurchRole)> GetCurrentUserChurchContextAsync()
        {
            // 1. Intentar obtener church_id directamente de los claims de la sesión
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value
                                ?? User.FindFirst("church_id")?.Value
                                ?? User.FindFirst("Church_Id")?.Value;

            Guid churchId = Guid.Empty;
            if (!string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid parsedClaimId))
            {
                churchId = parsedClaimId;
            }

            // 2. Obtener identificadores posibles del usuario en sesión
            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value
                            ?? User.FindFirst("email")?.Value
                            ?? User.FindFirst(ClaimTypes.Name)?.Value
                            ?? User.Identity?.Name ?? "";

            string userChurchRole = "MEMBER";

            if (!string.IsNullOrEmpty(userEmail))
            {
                // Consultar por email o por nombre para asegurar coincidencia con Supabase
                string query = @"
                    SELECT church_id, COALESCE(church_role, 'MEMBER') AS church_role 
                    FROM users 
                    WHERE email = @Email OR name = @Email OR email = @NameClaim OR name = @NameClaim 
                    LIMIT 1;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Email", userEmail),
                    new NpgsqlParameter("@NameClaim", User.Identity?.Name ?? "")
                };

                var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

                if (dt.Rows.Count > 0)
                {
                    if (churchId == Guid.Empty && dt.Rows[0]["church_id"] != DBNull.Value)
                    {
                        if (Guid.TryParse(dt.Rows[0]["church_id"].ToString(), out Guid dbChurchId))
                        {
                            churchId = dbChurchId;
                        }
                    }

                    if (dt.Rows[0]["church_role"] != DBNull.Value)
                    {
                        userChurchRole = dt.Rows[0]["church_role"].ToString()?.Trim().ToUpper() ?? "MEMBER";
                    }
                }
            }

            return (churchId, userChurchRole);
        }

        // 1. Listar miembros de la congregación
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var (churchId, userChurchRole) = await GetCurrentUserChurchContextAsync();

            if (churchId == Guid.Empty)
            {
                TempData["ErrorMessage"] = "No tienes una iglesia o ministerio asociado a tu cuenta.";
                return View(new DataTable());
            }

            TempData.Remove("ErrorMessage");

            // Validar si el usuario actual tiene permisos administrativos/pastorales en la iglesia
            bool canManageRoles = userChurchRole == "PASTOR" ||
                                 userChurchRole == "COPASTOR" ||
                                 userChurchRole == "ADMIN";

            string query = @"
                SELECT id, email, 
                       COALESCE(name, email) AS display_name, 
                       COALESCE(church_role, 'MEMBER') AS church_role, 
                       created_at 
                FROM users 
                WHERE church_id = @ChurchId 
                ORDER BY 
                    CASE UPPER(COALESCE(church_role, 'MEMBER'))
                        WHEN 'PASTOR' THEN 1
                        WHEN 'COPASTOR' THEN 2
                        WHEN 'LIDER' THEN 3
                        WHEN 'LEADER' THEN 3
                        ELSE 4
                    END ASC, email ASC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.CanManageRoles = canManageRoles;
            ViewBag.CurrentChurchRole = userChurchRole;

            return View(dt);
        }

        // 2. Cambiar Rol Eclesial de un Usuario
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateRole(Guid userId, string newRole)
        {
            var (churchId, userChurchRole) = await GetCurrentUserChurchContextAsync();

            bool canManageRoles = userChurchRole == "PASTOR" ||
                                 userChurchRole == "COPASTOR" ||
                                 userChurchRole == "ADMIN";

            if (churchId == Guid.Empty || !canManageRoles)
            {
                TempData["ErrorMessage"] = "No tienes permisos de Pastor o Co-Pastor para modificar usuarios de esta iglesia.";
                return RedirectToAction(nameof(Index));
            }

            string roleUpper = newRole.Trim().ToUpper();
            if (roleUpper != "COPASTOR" && roleUpper != "LIDER" && roleUpper != "LEADER" && roleUpper != "MEMBER" && roleUpper != "MIEMBRO" && roleUpper != "PASTOR")
            {
                TempData["ErrorMessage"] = "Rol ministerial no válido.";
                return RedirectToAction(nameof(Index));
            }

            if (roleUpper == "MIEMBRO") roleUpper = "MEMBER";
            if (roleUpper == "LEADER") roleUpper = "LIDER";

            string query = "UPDATE users SET church_role = @Role WHERE id = @UserId AND church_id = @ChurchId;";
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Role", roleUpper),
                new NpgsqlParameter("@UserId", userId),
                new NpgsqlParameter("@ChurchId", churchId)
            };

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            TempData["SuccessMessage"] = "Rol ministerial actualizado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            var (churchId, userChurchRole) = await GetCurrentUserChurchContextAsync();

            bool canManageRoles = userChurchRole == "PASTOR" ||
                                 userChurchRole == "COPASTOR" ||
                                 userChurchRole == "ADMIN";

            if (churchId == Guid.Empty || !canManageRoles)
            {
                TempData["ErrorMessage"] = "No tienes permisos para exportar la lista de miembros.";
                return RedirectToAction(nameof(Index));
            }

            string query = @"
        SELECT COALESCE(name, email) AS nombre, 
               email, 
               COALESCE(church_role, 'MEMBER') AS rol_eclesial, 
               created_at AS fecha_vinculacion 
        FROM users 
        WHERE church_id = @ChurchId 
        ORDER BY created_at DESC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Nombre,Correo,Rol Eclesial,Fecha Vinculacion");

            foreach (DataRow row in dt.Rows)
            {
                string name = $"\"{row["nombre"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string email = $"\"{row["email"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string role = $"\"{row["rol_eclesial"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string date = row["fecha_vinculacion"] != DBNull.Value
                    ? Convert.ToDateTime(row["fecha_vinculacion"]).ToString("yyyy-MM-dd HH:mm")
                    : "";

                builder.AppendLine($"{name},{email},{role},{date}");
            }

            // Encoding UTF8 con preamble (BOM) para compatibilidad con Excel
            byte[] buffer = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString())).ToArray();

            return File(buffer, "text/csv", $"Miembros_Iglesia_{DateTime.Now:yyyyMMdd}.csv");
        }
    }
}