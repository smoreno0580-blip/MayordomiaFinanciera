using System;
using System.Security.Claims;
using System.Threading.Tasks;
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

        public CalendarController(PostgresRepository postgresRepository)
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

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        // Método auxiliar para evaluar si el usuario tiene privilegios administrativos o ministeriales (Rol de Iglesia + Rol General)
        private async Task<bool> UserCanManageCalendarAsync(Guid churchId)
        {
            // 1. Evaluar roles generales del sistema
            if (User.IsInRole("admin") || User.IsInRole("ADMIN") ||
                User.IsInRole("pastor") || User.IsInRole("PASTOR") ||
                User.IsInRole("copastor") || User.IsInRole("COPASTOR"))
            {
                return true;
            }

            // 2. Evaluar el rol específico de la iglesia desde la base de datos (tabla users)
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

                    // Permitir si el rol eclesial es Pastor, Admin, Copastor, Líder o Profesor (Teacher)
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

            // Evaluar permisos basados tanto en el sistema como en el rol de la iglesia
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

        // ➕ 3. Guardar o Actualizar Evento (Upsert)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveEvent(CalendarEventDto model)
        {
            Guid churchId = GetCurrentChurchId();
            Guid userId = GetCurrentUserId();

            if (churchId == Guid.Empty) return Unauthorized();

            // Validar permisos del rol eclesial antes de permitir la escritura
            if (!await UserCanManageCalendarAsync(churchId))
            {
                TempData["ErrorMessage"] = "No tienes permisos de rol eclesial para modificar el calendario.";
                return RedirectToAction(nameof(Index));
            }

            // 🔍 DIAGNÓSTICO DE ERRORES DEL MODELSTATE
            if (!ModelState.IsValid)
            {
                var errorList = new System.Collections.Generic.List<string>();
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

            if (!model.Id.HasValue || model.Id.Value == Guid.Empty)
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
                TempData["SuccessMessage"] = "¡Evento agendado exitosamente!";
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
    }
}