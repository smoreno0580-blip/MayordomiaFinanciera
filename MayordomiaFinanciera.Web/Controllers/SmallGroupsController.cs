using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Web.Models.Church;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class SmallGroupsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public SmallGroupsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        // Obtener el ChurchId del usuario autenticado
        private Guid GetCurrentChurchId()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            if (!string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid churchId))
            {
                return churchId;
            }
            return Guid.Empty;
        }

        // Obtener el UserId o nombre del usuario actual
        private string GetCurrentUserName()
        {
            return User.Identity?.Name ?? "Líder Ministerial";
        }

        // 1. Listado principal de grupos (Vista adaptada por rol)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty)
            {
                TempData["ErrorMessage"] = "No tienes una congregación asociada en tu sesión.";
                return RedirectToAction("Index", "Home");
            }

            bool isPastorOrAdmin = User.IsInRole("admin") || User.IsInRole("ADMIN") ||
                                   User.IsInRole("PASTOR") || User.IsInRole("pastor") ||
                                   User.IsInRole("copastor") || User.IsInRole("COPASTOR");

            string query = isPastorOrAdmin
                ? "SELECT * FROM church_small_groups WHERE church_id = @ChurchId ORDER BY name;"
                : "SELECT * FROM church_small_groups WHERE church_id = @ChurchId AND is_active = true ORDER BY name;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.IsPastorOrAdmin = isPastorOrAdmin;
            return View(dt);
        }

        // 2. [GET] Crear o Editar Grupo (Solo Pastores / Co-pastores)
        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,copastor,COPASTOR,lider,LIDER,teacher,TEACHER")]
        [HttpGet]
        public async Task<IActionResult> Upsert(Guid? id)
        {
            Guid churchId = GetCurrentChurchId();
            var model = new SmallGroupDto { ChurchId = churchId };

            if (id.HasValue && id.Value != Guid.Empty)
            {
                string query = "SELECT * FROM church_small_groups WHERE id = @Id AND church_id = @ChurchId;";
                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@ChurchId", churchId)
                };

                var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);
                if (dt.Rows.Count > 0)
                {
                    var row = dt.Rows[0];
                    model.Id = Guid.Parse(row["id"].ToString()!);
                    model.Name = row["name"]?.ToString() ?? "";
                    model.Description = row["description"]?.ToString();
                    model.MeetingDay = row["meeting_day"]?.ToString();
                    model.MeetingTime = row["meeting_time"] != DBNull.Value ? (TimeSpan?)row["meeting_time"] : null;
                    model.Location = row["location"]?.ToString();
                    model.LeaderName = row["leader_name"]?.ToString();
                    model.IsActive = row["is_active"] == DBNull.Value || Convert.ToBoolean(row["is_active"]);
                }
                else
                {
                    return NotFound();
                }
            }

            return View(model);
        }

        // 3. [POST] Guardar Grupo
        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,copastor,COPASTOR,lider,LIDER,teacher,TEACHER")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(SmallGroupDto model)
        {
            Guid churchId = GetCurrentChurchId();
            model.ChurchId = churchId;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.Id == Guid.Empty)
            {
                // Insertar nuevo grupo
                string insertQuery = @"
                    INSERT INTO church_small_groups (church_id, name, description, meeting_day, meeting_time, location, leader_name, is_active)
                    VALUES (@ChurchId, @Name, @Description, @MeetingDay, @MeetingTime, @Location, @LeaderName, @IsActive);";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Name", model.Name ?? ""),
                    new NpgsqlParameter("@Description", (object?)model.Description ?? DBNull.Value),
                    new NpgsqlParameter("@MeetingDay", (object?)model.MeetingDay ?? DBNull.Value),
                    new NpgsqlParameter("@MeetingTime", model.MeetingTime.HasValue ? (object)model.MeetingTime.Value : DBNull.Value),
                    new NpgsqlParameter("@Location", (object?)model.Location ?? DBNull.Value),
                    new NpgsqlParameter("@LeaderName", (object?)model.LeaderName ?? DBNull.Value),
                    new NpgsqlParameter("@IsActive", model.IsActive)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);
                TempData["SuccessMessage"] = "Grupo pequeño creado exitosamente.";
            }
            else
            {
                // Actualizar grupo existente
                string updateQuery = @"
                    UPDATE church_small_groups 
                    SET name = @Name, description = @Description, meeting_day = @MeetingDay, 
                        meeting_time = @MeetingTime, location = @Location, leader_name = @LeaderName, is_active = @IsActive
                    WHERE id = @Id AND church_id = @ChurchId;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", model.Id),
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Name", model.Name ?? ""),
                    new NpgsqlParameter("@Description", (object?)model.Description ?? DBNull.Value),
                    new NpgsqlParameter("@MeetingDay", (object?)model.MeetingDay ?? DBNull.Value),
                    new NpgsqlParameter("@MeetingTime", model.MeetingTime.HasValue ? (object)model.MeetingTime.Value : DBNull.Value),
                    new NpgsqlParameter("@Location", (object?)model.Location ?? DBNull.Value),
                    new NpgsqlParameter("@LeaderName", (object?)model.LeaderName ?? DBNull.Value),
                    new NpgsqlParameter("@IsActive", model.IsActive)
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
                TempData["SuccessMessage"] = "Grupo pequeño actualizado exitosamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        // 4. [GET] Registrar Asistencia Semanal (Para Líderes o Pastores)
        [HttpGet]
        public async Task<IActionResult> AttendanceReport(Guid groupId)
        {
            Guid churchId = GetCurrentChurchId();

            // Verificar que el grupo pertenezca a la iglesia
            string groupQuery = "SELECT * FROM church_small_groups WHERE id = @Id AND church_id = @ChurchId;";
            var groupParams = new NpgsqlParameter[] { new NpgsqlParameter("@Id", groupId), new NpgsqlParameter("@ChurchId", churchId) };
            var groupDt = await _postgresRepository.ExecuteQueryAsync(groupQuery, groupParams);

            if (groupDt.Rows.Count == 0) return NotFound();

            ViewBag.GroupName = groupDt.Rows[0]["name"]?.ToString();

            // Obtener historial de reportes de asistencia de este grupo
            string reportQuery = "SELECT * FROM church_group_attendance_reports WHERE group_id = @GroupId ORDER BY meeting_date DESC LIMIT 10;";
            var reportParams = new NpgsqlParameter[] { new NpgsqlParameter("@GroupId", groupId) };
            var historyDt = await _postgresRepository.ExecuteQueryAsync(reportQuery, reportParams);

            var attendanceModel = new GroupAttendanceDto
            {
                GroupId = groupId,
                ChurchId = churchId,
                MeetingDate = DateTime.Today
            };

            ViewBag.History = historyDt;
            return View(attendanceModel);
        }

        // 5. [POST] Guardar Reporte de Asistencia
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AttendanceReport(GroupAttendanceDto model)
        {
            Guid churchId = GetCurrentChurchId();
            model.ChurchId = churchId;
            model.ReportedBy = GetCurrentUserName();

            string query = @"
                INSERT INTO church_group_attendance_reports (group_id, church_id, meeting_date, attendee_count, notes, reported_by)
                VALUES (@GroupId, @ChurchId, @MeetingDate, @AttendeeCount, @Notes, @ReportedBy);";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@GroupId", model.GroupId),
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@MeetingDate", model.MeetingDate),
                new NpgsqlParameter("@AttendeeCount", model.AttendeeCount),
                new NpgsqlParameter("@Notes", (object?)model.Notes ?? DBNull.Value),
                new NpgsqlParameter("@ReportedBy", model.ReportedBy)
            };

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            TempData["SuccessMessage"] = "Reporte de asistencia registrado con éxito. ¡Gracias por mantener el pulso del grupo!";

            return RedirectToAction(nameof(AttendanceReport), new { groupId = model.GroupId });
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index");

            // Consulta SQL a prueba de fallos usando exclusivamente los campos existentes
            string query = @"
        SELECT name, 
               COALESCE(leader_name, '') AS leader_name, 
               COALESCE(location, '') AS location, 
               COALESCE(meeting_day, '') AS meeting_day, 
               meeting_time::text AS meeting_time 
        FROM church_small_groups 
        WHERE church_id = @ChurchId 
        ORDER BY name ASC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Nombre Celula,Lider,Ubicacion/Direccion,Dia Reunion,Hora,Estado");

            foreach (DataRow row in dt.Rows)
            {
                string name = $"\"{row["name"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string leader = $"\"{row["leader_name"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string location = $"\"{row["location"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string day = $"\"{row["meeting_day"]?.ToString()?.Replace("\"", "\"\"")}\"";

                string rawTime = row["meeting_time"] != DBNull.Value ? row["meeting_time"]?.ToString() ?? "" : "";
                string time = $"\"{rawTime.Replace("\"", "\"\"")}\"";

                string status = "\"Activo\"";

                builder.AppendLine($"{name},{leader},{location},{day},{time},{status}");
            }

            byte[] fileBytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString())).ToArray();

            return File(fileBytes, "text/csv", $"Celulas_Grupos_{DateTime.Now:yyyyMMdd}.csv");
        }

        [HttpGet]
        public async Task<IActionResult> ExportAttendanceCsv(Guid groupId)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || groupId == Guid.Empty)
            {
                return RedirectToAction(nameof(Index));
            }

            // Obtener el nombre del grupo para el título del archivo CSV
            string groupQuery = "SELECT name FROM church_small_groups WHERE id = @GroupId AND church_id = @ChurchId LIMIT 1;";
            var groupParams = new NpgsqlParameter[]
            {
        new NpgsqlParameter("@GroupId", groupId),
        new NpgsqlParameter("@ChurchId", churchId)
            };
            var dtGroup = await _postgresRepository.ExecuteQueryAsync(groupQuery, groupParams);
            string groupName = dtGroup.Rows.Count > 0 ? dtGroup.Rows[0]["name"]?.ToString() ?? "Celula" : "Celula";

            // Consultar la tabla correcta: church_group_attendance_reports
            string attendanceQuery = @"
        SELECT meeting_date, 
               COALESCE(attendee_count, 0) AS attendee_count, 
               COALESCE(notes, '') AS notes, 
               COALESCE(reported_by, '') AS reported_by 
        FROM church_group_attendance_reports 
        WHERE group_id = @GroupId AND church_id = @ChurchId 
        ORDER BY meeting_date DESC;";

            var parameters = new NpgsqlParameter[]
            {
        new NpgsqlParameter("@GroupId", groupId),
        new NpgsqlParameter("@ChurchId", churchId)
            };

            var dt = await _postgresRepository.ExecuteQueryAsync(attendanceQuery, parameters);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Fecha Reunion,Total Asistentes,Notas / Motivos de Oracion,Reportado Por");

            foreach (DataRow row in dt.Rows)
            {
                string date = row["meeting_date"] != DBNull.Value
                    ? Convert.ToDateTime(row["meeting_date"]).ToString("yyyy-MM-dd")
                    : "";
                string count = row["attendee_count"]?.ToString() ?? "0";
                string notes = $"\"{row["notes"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string reportedBy = $"\"{row["reported_by"]?.ToString()?.Replace("\"", "\"\"")}\"";

                builder.AppendLine($"{date},{count},{notes},{reportedBy}");
            }

            byte[] fileBytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString())).ToArray();

            string safeGroupName = string.Join("_", groupName.Split(System.IO.Path.GetInvalidFileNameChars()));
            return File(fileBytes, "text/csv", $"Asistencia_{safeGroupName}_{DateTime.Now:yyyyMMdd}.csv");
        }
    }
}