using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class ChurchController : Controller
    {
        private readonly IChurchRepository _churchRepository;
        private readonly PostgresRepository _postgresRepository;

        public ChurchController(IChurchRepository churchRepository, PostgresRepository postgresRepository)
        {
            _churchRepository = churchRepository;
            _postgresRepository = postgresRepository;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> JoinByCode([FromBody] JoinChurchRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Code))
            {
                return Json(new { success = false, message = "Por favor ingrese un código de invitación válido." });
            }

            try
            {
                Guid userId = GetCurrentUserId();
                bool linked = await _churchRepository.LinkUserToChurchByCodeAsync(userId, request.Code);

                if (linked)
                {
                    return Json(new { success = true, message = "¡Te has vinculado exitosamente a tu iglesia! Se han desbloqueado tus beneficios." });
                }

                return Json(new { success = false, message = "El código ingresado no existe o no se encuentra activo." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al vincular: {ex.Message}" });
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> LeaveChurch()
        {
            try
            {
                Guid userId = GetCurrentUserId();

                // 1. Obtener datos del usuario e iglesia actual para la auditoría antes de desvincular
                string userQuery = "SELECT name, email, church_id FROM users WHERE id = @UserId AND church_id IS NOT NULL LIMIT 1;";
                var dtUser = await _postgresRepository.ExecuteQueryAsync(userQuery, new NpgsqlParameter[] { new NpgsqlParameter("@UserId", userId) });

                if (dtUser.Rows.Count > 0)
                {
                    var row = dtUser.Rows[0];
                    Guid churchId = Guid.Parse(row["church_id"].ToString()!);
                    string userName = row["name"]?.ToString() ?? "";
                    string userEmail = row["email"]?.ToString() ?? "";

                    // 2. Registrar el evento en la tabla de auditoría church_leaves
                    string insertLeaveQuery = @"
                        INSERT INTO church_leaves (user_id, user_name, user_email, church_id, leave_date, reason)
                        VALUES (@UserId, @UserName, @UserEmail, @ChurchId, NOW(), 'Desvinculación voluntaria');";

                    await _postgresRepository.ExecuteNonQueryAsync(insertLeaveQuery, new NpgsqlParameter[]
                    {
                        new NpgsqlParameter("@UserId", userId),
                        new NpgsqlParameter("@UserName", userName),
                        new NpgsqlParameter("@UserEmail", userEmail),
                        new NpgsqlParameter("@ChurchId", churchId)
                    });
                }

                // 3. Desvincular al usuario en BD: Limpia church_id, asigna rol 'student' y reinicia church_role
                string updateQuery = @"
                    UPDATE users 
                    SET church_id = NULL, 
                        role = 'student',
                        church_role = NULL 
                    WHERE id = @UserId;";

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, new NpgsqlParameter[] { new NpgsqlParameter("@UserId", userId) });

                // Sincronizar repositorio si existe abstracción
                await _churchRepository.UnlinkUserFromChurchAsync(userId);

                // 4. Re-emitir los claims de sesión HTTP para actualizar la Cookie activa a rol 'student'
                string userEmailClaim = User.FindFirst(ClaimTypes.Email)?.Value
                                        ?? User.FindFirst(ClaimTypes.Name)?.Value
                                        ?? User.Identity?.Name
                                        ?? "";

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Name, User.Identity?.Name ?? ""),
                    new Claim(ClaimTypes.Email, userEmailClaim),
                    new Claim(ClaimTypes.Role, "student") // Asignación explicita del nuevo rol student
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity)
                );

                return Json(new { success = true, message = "Te has desvinculado de la iglesia exitosamente. Tu cuenta ahora tiene rol de estudiante." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al desvincularte: {ex.Message}" });
            }
        }

        [HttpGet]
        public async Task<IActionResult> ExportLeavesReportCsv()
        {
            try
            {
                Guid userId = GetCurrentUserId();

                string churchQuery = "SELECT church_id FROM users WHERE id = @UserId AND church_id IS NOT NULL LIMIT 1;";
                var dtUser = await _postgresRepository.ExecuteQueryAsync(churchQuery, new NpgsqlParameter[] { new NpgsqlParameter("@UserId", userId) });

                if (dtUser.Rows.Count == 0)
                {
                    return BadRequest("No estás asociado a ninguna iglesia.");
                }

                Guid churchId = Guid.Parse(dtUser.Rows[0]["church_id"].ToString()!);

                string query = @"
                    SELECT user_name, user_email, leave_date, reason 
                    FROM church_leaves 
                    WHERE church_id = @ChurchId 
                    ORDER BY leave_date DESC;";

                var dtLeaves = await _postgresRepository.ExecuteQueryAsync(query, new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) });

                var csvBuilder = new StringBuilder();
                csvBuilder.AppendLine("Nombre del Miembro,Correo Electrónico,Fecha de Desvinculación,Motivo");

                foreach (DataRow row in dtLeaves.Rows)
                {
                    string name = $"\"{row["user_name"]?.ToString()?.Replace("\"", "\"\"")}\"";
                    string email = row["user_email"]?.ToString() ?? "";
                    string leaveDate = row["leave_date"] != DBNull.Value ? Convert.ToDateTime(row["leave_date"]).ToString("yyyy-MM-dd HH:mm:ss") : "N/A";
                    string reason = $"\"{row["reason"]?.ToString()?.Replace("\"", "\"\"")}\"";

                    csvBuilder.AppendLine($"{name},{email},{leaveDate},{reason}");
                }

                byte[] buffer = Encoding.UTF8.GetBytes(csvBuilder.ToString());
                string fileName = $"Informe_Miembros_Desvinculados_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

                return File(buffer, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                return BadRequest($"Error al generar el informe: {ex.Message}");
            }
        }
    }
}