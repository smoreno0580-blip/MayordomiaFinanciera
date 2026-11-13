using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class ChurchEmailController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public ChurchEmailController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        private Guid GetCurrentChurchId()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            if (!string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid churchId))
            {
                return churchId;
            }
            return Guid.Empty;
        }

        // 🔐 Validación de permisos pastorales o administrativos
        private async Task<bool> UserCanManageChurchSettingsAsync()
        {
            if (User.IsInRole("admin") || User.IsInRole("ADMIN") ||
                User.IsInRole("pastor") || User.IsInRole("PASTOR") ||
                User.IsInRole("copastor") || User.IsInRole("COPASTOR"))
            {
                return true;
            }

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value
                               ?? User.FindFirst(ClaimTypes.Name)?.Value
                               ?? User.Identity?.Name ?? "";

            if (!string.IsNullOrEmpty(userEmail))
            {
                string query = "SELECT church_role FROM users WHERE (email = @Email OR name = @Email) LIMIT 1;";
                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Email", userEmail) };
                var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

                if (dt.Rows.Count > 0 && dt.Rows[0]["church_role"] != DBNull.Value)
                {
                    string churchRole = dt.Rows[0]["church_role"]?.ToString()?.ToUpper() ?? "";
                    if (churchRole.Contains("PASTOR") || churchRole.Contains("ADMIN") || churchRole.Contains("COPASTOR"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // ⚙️ 1. Vista de Configuración SMTP
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            if (!await UserCanManageChurchSettingsAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos de administración para configurar el servidor de correo.";
                return RedirectToAction("Index", "Dashboard");
            }

            string query = @"
                SELECT smtp_host, smtp_port, smtp_user, smtp_password, smtp_sender_email, smtp_sender_name, use_custom_smtp
                FROM church_email_settings 
                WHERE church_id = @ChurchId 
                LIMIT 1;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.CurrentChurchId = churchId;
            return View(dt);
        }

        // 💾 2. Guardar o Actualizar Configuración SMTP (Upsert)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            string smtpHost,
            int smtpPort,
            string smtpUser,
            string smtpPassword,
            string smtpSenderEmail,
            string smtpSenderName,
            bool useCustomSmtp)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return Unauthorized();

            if (!await UserCanManageChurchSettingsAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para modificar la configuración de correo.";
                return RedirectToAction(nameof(Index));
            }

            // Upsert en PostgreSQL utilizando ON CONFLICT (church_id)
            string upsertQuery = @"
                INSERT INTO church_email_settings 
                (church_id, smtp_host, smtp_port, smtp_user, smtp_password, smtp_sender_email, smtp_sender_name, use_custom_smtp, updated_at)
                VALUES 
                (@ChurchId, @SmtpHost, @SmtpPort, @SmtpUser, @SmtpPassword, @SmtpSenderEmail, @SmtpSenderName, @UseCustomSmtp, CURRENT_TIMESTAMP)
                ON CONFLICT (church_id) 
                DO UPDATE SET 
                    smtp_host = EXCLUDED.smtp_host,
                    smtp_port = EXCLUDED.smtp_port,
                    smtp_user = EXCLUDED.smtp_user,
                    smtp_password = EXCLUDED.smtp_password,
                    smtp_sender_email = EXCLUDED.smtp_sender_email,
                    smtp_sender_name = EXCLUDED.smtp_sender_name,
                    use_custom_smtp = EXCLUDED.use_custom_smtp,
                    updated_at = CURRENT_TIMESTAMP;";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@SmtpHost", (object?)smtpHost ?? DBNull.Value),
                new NpgsqlParameter("@SmtpPort", smtpPort > 0 ? smtpPort : 587),
                new NpgsqlParameter("@SmtpUser", (object?)smtpUser ?? DBNull.Value),
                new NpgsqlParameter("@SmtpPassword", (object?)smtpPassword ?? DBNull.Value),
                new NpgsqlParameter("@SmtpSenderEmail", (object?)smtpSenderEmail ?? DBNull.Value),
                new NpgsqlParameter("@SmtpSenderName", (object?)smtpSenderName ?? DBNull.Value),
                new NpgsqlParameter("@UseCustomSmtp", useCustomSmtp)
            };

            await _postgresRepository.ExecuteNonQueryAsync(upsertQuery, parameters);
            TempData["SuccessMessage"] = "¡Configuración de correo SMTP actualizada exitosamente!";

            return RedirectToAction(nameof(Index));
        }
    }
}