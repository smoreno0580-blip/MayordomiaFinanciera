using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Core.Interfaces;
using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Web.Models.Church;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class CalendarController : Controller
    {
        private readonly PostgresRepository _postgresRepository;
        private readonly IEmailService _emailService;

        public CalendarController(PostgresRepository postgresRepository, IEmailService emailService)
        {
            _postgresRepository = postgresRepository;
            _emailService = emailService;
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

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        // Método auxiliar para evaluar si el usuario tiene privilegios administrativos o ministeriales
        private async Task<bool> UserCanManageCalendarAsync(Guid churchId)
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

                    if (churchRole.Contains("PASTOR") || churchRole.Contains("ADMIN") ||
                        churchRole.Contains("COPASTOR") || churchRole.Contains("LIDER") ||
                        churchRole.Contains("TEACHER") || churchRole.Contains("PROFESOR"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // 📅 1. Vista Principal del Calendario
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            string query = @"
                SELECT id, church_id, title, description, category, 
                       start_date, end_date, location, is_virtual, meeting_link, created_by, created_at
                FROM church_calendar_events
                WHERE church_id = @ChurchId
                ORDER BY start_date ASC;";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@ChurchId", churchId)
            };

            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.IsPastorOrAdmin = await UserCanManageCalendarAsync(churchId);
            ViewBag.CurrentChurchId = churchId;

            return View(dt);
        }

        // 📡 2. Endpoint API JSON para el Widget del Dashboard o FullCalendar
        [HttpGet]
        public async Task<IActionResult> GetEventsJson(DateTime? start, DateTime? end)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return Json(new object[] { });

            DateTime startDate = start ?? DateTime.Now.AddMonths(-1);
            DateTime endDate = end ?? DateTime.Now.AddMonths(2);

            string query = @"
                SELECT id, title, description, category, start_date AS start, end_date AS ""end"", 
                       location, is_virtual, meeting_link
                FROM church_calendar_events
                WHERE church_id = @ChurchId AND start_date >= @StartDate AND end_date <= @EndDate
                ORDER BY start_date ASC;";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@StartDate", startDate),
                new NpgsqlParameter("@EndDate", endDate)
            };

            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);
            return Json(dt);
        }

        // ➕ 3. Guardar o Actualizar Evento (Upsert con Notificación por Correo)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEvent(CalendarEventDto model)
        {
            Guid churchId = GetCurrentChurchId();
            Guid userId = GetCurrentUserId();

            if (churchId == Guid.Empty) return Unauthorized();

            if (!await UserCanManageCalendarAsync(churchId))
            {
                TempData["ErrorMessage"] = "No tienes permisos de rol eclesial para modificar el calendario.";
                return RedirectToAction(nameof(Index));
            }

            if (!ModelState.IsValid)
            {
                var errorList = new List<string>();
                foreach (var state in ModelState)
                {
                    foreach (var error in state.Value.Errors)
                    {
                        errorList.Add($"{state.Key}: {error.ErrorMessage}");
                    }
                }
                TempData["ErrorMessage"] = "Error de validación -> " + string.Join(" | ", errorList);
                return RedirectToAction(nameof(Index));
            }

            bool isNewEvent = !model.Id.HasValue || model.Id.Value == Guid.Empty;

            if (isNewEvent)
            {
                // Crear evento
                string insertQuery = @"
                    INSERT INTO church_calendar_events 
                    (church_id, title, description, category, start_date, end_date, location, is_virtual, meeting_link, created_by)
                    VALUES 
                    (@ChurchId, @Title, @Description, @Category, @StartDate, @EndDate, @Location, @IsVirtual, @MeetingLink, @CreatedBy);";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Title", model.Title),
                    new NpgsqlParameter("@Description", (object?)model.Description ?? DBNull.Value),
                    new NpgsqlParameter("@Category", model.Category),
                    new NpgsqlParameter("@StartDate", model.StartDate),
                    new NpgsqlParameter("@EndDate", model.EndDate),
                    new NpgsqlParameter("@Location", (object?)model.Location ?? DBNull.Value),
                    new NpgsqlParameter("@IsVirtual", model.IsVirtual),
                    new NpgsqlParameter("@MeetingLink", (object?)model.MeetingLink ?? DBNull.Value),
                    new NpgsqlParameter("@CreatedBy", userId)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);

                // 📩 NOTIFICAR A LA MEMBRESÍA POR CORREO AL CREAR UN NUEVO EVENTO
                string churchName = await GetChurchNameAsync(churchId);
                var members = await GetChurchMembersAsync(churchId);

                string eventTitle = model.Title ?? "Nuevo Evento Eclesial";
                string eventDateStr = model.StartDate.ToString("dd/MM/yyyy hh:mm tt");
                string eventLocation = !string.IsNullOrWhiteSpace(model.Location) ? model.Location : "Instalaciones de la Iglesia";
                string eventDesc = model.Description ?? "";

                _ = Task.Run(async () =>
                {
                    foreach (var member in members)
                    {
                        try
                        {
                            string subject = $"Nuevo Evento Agendado: {eventTitle}";
                            string htmlBody = $@"
                                <div style='font-family: Arial, sans-serif; padding: 20px; color: #1B365D;'>
                                    <h2>¡Hola, {member.FullName}!</h2>
                                    <p>Te informamos que <strong>{churchName}</strong> ha programado un nuevo evento en su calendario:</p>
                                    <div style='background-color: #f8fafc; border-left: 4px solid #00A8CC; padding: 15px; margin: 15px 0;'>
                                        <h3 style='margin-top: 0; color: #1B365D;'>{eventTitle}</h3>
                                        <p><strong>📅 Fecha y Hora:</strong> {eventDateStr}</p>
                                        <p><strong>📍 Lugar:</strong> {eventLocation}</p>
                                        {(model.IsVirtual ? $"<p><strong>💻 Enlace Virtual:</strong> <a href='{model.MeetingLink}'>Unirse a la reunión</a></p>" : "")}
                                        {(!string.IsNullOrEmpty(eventDesc) ? $"<p><strong>📝 Detalles:</strong> {eventDesc}</p>" : "")}
                                    </div>
                                    <p style='font-size: 11px; color: #666;'>Te esperamos. ¡Dios te bendiga!</p>
                                </div>";

                            // Envío usando el servicio unificado (evalúa automáticamente SMTP personalizado de la iglesia o general)
                            await _emailService.SendEmailAsync(member.Email, subject, htmlBody);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"Error enviando notificación de calendario a {member.Email}: {ex.Message}");
                        }
                    }
                });

                TempData["SuccessMessage"] = "¡Evento agendado exitosamente y notificado a la congregación!";
            }
            else
            {
                // Editar evento existente
                string updateQuery = @"
                    UPDATE church_calendar_events
                    SET title = @Title, description = @Description, category = @Category,
                        start_date = @StartDate, end_date = @EndDate, location = @Location,
                        is_virtual = @IsVirtual, meeting_link = @MeetingLink, updated_at = CURRENT_TIMESTAMP
                    WHERE id = @Id AND church_id = @ChurchId;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", model.Id),
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Title", model.Title),
                    new NpgsqlParameter("@Description", (object?)model.Description ?? DBNull.Value),
                    new NpgsqlParameter("@Category", model.Category),
                    new NpgsqlParameter("@StartDate", model.StartDate),
                    new NpgsqlParameter("@EndDate", model.EndDate),
                    new NpgsqlParameter("@Location", (object?)model.Location ?? DBNull.Value),
                    new NpgsqlParameter("@IsVirtual", model.IsVirtual),
                    new NpgsqlParameter("@MeetingLink", (object?)model.MeetingLink ?? DBNull.Value)
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
                TempData["SuccessMessage"] = "¡Evento actualizado correctamente!";
            }

            return RedirectToAction(nameof(Index));
        }

        // 🗑️ 4. Eliminar Evento
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteEvent(Guid id)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || id == Guid.Empty) return RedirectToAction(nameof(Index));

            if (!await UserCanManageCalendarAsync(churchId))
            {
                TempData["ErrorMessage"] = "No tienes permisos de rol eclesial para eliminar eventos.";
                return RedirectToAction(nameof(Index));
            }

            string deleteQuery = "DELETE FROM church_calendar_events WHERE id = @Id AND church_id = @ChurchId;";
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Id", id),
                new NpgsqlParameter("@ChurchId", churchId)
            };

            await _postgresRepository.ExecuteNonQueryAsync(deleteQuery, parameters);
            TempData["SuccessMessage"] = "Evento eliminado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        #region Métodos Auxiliares

        private async Task<string> GetChurchNameAsync(Guid churchId)
        {
            string query = "SELECT name FROM churches WHERE id = @ChurchId;";
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            if (dt.Rows.Count > 0)
            {
                return dt.Rows[0]["name"]?.ToString() ?? "Nuestra Iglesia";
            }
            return "Nuestra Iglesia";
        }

        private async Task<List<(string Email, string FullName)>> GetChurchMembersAsync(Guid churchId)
        {
            var members = new List<(string Email, string FullName)>();

            // 🎯 Consulta ajustada exactamente a los campos reales de la tabla users de Supabase (id, name, email, church_id)
            string query = @"
                SELECT email, name 
                FROM users 
                WHERE church_id = @ChurchId 
                  AND email IS NOT NULL 
                  AND email != '';";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            foreach (DataRow row in dt.Rows)
            {
                string email = row["email"]?.ToString() ?? "";
                string fullName = row["name"]?.ToString() ?? "Estimado(a) Miembro";

                if (!string.IsNullOrWhiteSpace(email))
                {
                    members.Add((email, fullName));
                }
            }

            return members;
        }

        #endregion
    }
}