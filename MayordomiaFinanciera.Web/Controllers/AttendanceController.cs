using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class AttendanceController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public AttendanceController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        [Authorize]
        public async Task<IActionResult> Index(Guid? groupId, Guid? sessionId)
        {
            // 1. Obtener lista de grupos activos
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups WHERE is_active = true ORDER BY name", null);
            ViewBag.GroupsList = dtGroups;

            DataTable dtStudents = new DataTable();
            DataTable dtSessions = new DataTable();

            if (groupId.HasValue && groupId != Guid.Empty)
            {
                // 2. Obtener estudiantes del grupo seleccionado
                var groupParams1 = new NpgsqlParameter[] { new NpgsqlParameter("@GroupId", groupId.Value) };
                dtStudents = await _postgresRepository.ExecuteQueryAsync(
                    "SELECT * FROM users WHERE group_id = @GroupId AND role = 'student' ORDER BY name",
                    groupParams1);

                // 3. Obtener sesiones del grupo seleccionado
                var groupParams2 = new NpgsqlParameter[] { new NpgsqlParameter("@GroupId", groupId.Value) };
                dtSessions = await _postgresRepository.ExecuteQueryAsync(
                    "SELECT * FROM sessions WHERE group_id = @GroupId ORDER BY date DESC",
                    groupParams2);
            }

            ViewBag.SelectedGroupId = groupId;
            ViewBag.SelectedSessionId = sessionId;
            ViewBag.SessionsList = dtSessions;

            // 4. Obtener registros de asistencia si hay una sesión seleccionada
            DataTable dtAttendance = new DataTable();
            if (sessionId.HasValue && sessionId != Guid.Empty)
            {
                var sessionParams = new NpgsqlParameter[] { new NpgsqlParameter("@SessionId", sessionId.Value) };
                dtAttendance = await _postgresRepository.ExecuteQueryAsync(
                    "SELECT * FROM attendance WHERE session_id = @SessionId",
                    sessionParams);
            }
            ViewBag.AttendanceRecords = dtAttendance;

            return View(dtStudents);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(Guid sessionId, Guid userId, string status, Guid? currentGroupId)
        {
            // Llamada a la función PL/pgSQL sp_upsert_attendance que creamos en Supabase
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@p_session_id", sessionId),
                new NpgsqlParameter("@p_user_id", userId),
                new NpgsqlParameter("@p_status", status ?? "present")
            };

            await _postgresRepository.ExecuteNonQueryAsync("SELECT sp_upsert_attendance(@p_session_id, @p_user_id, @p_status)", parameters);

            return RedirectToAction("Index", new { groupId = currentGroupId, sessionId });
        }
    }
}