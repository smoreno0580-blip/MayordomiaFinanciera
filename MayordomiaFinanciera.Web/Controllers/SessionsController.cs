using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class SessionsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public SessionsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            string userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            string userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

            DataTable sessionsTable;

            if (userRole == "student")
            {
                // 1. Obtenemos el group_id del estudiante logueado (UUID de Supabase)
                var userQuery = "SELECT group_id FROM users WHERE id = @UserId;";
                var userParams = new NpgsqlParameter[] { new NpgsqlParameter("@UserId", Guid.Parse(userId)) };
                DataTable userDt = await _postgresRepository.ExecuteQueryAsync(userQuery, userParams);

                object studentGroupId = DBNull.Value;
                if (userDt.Rows.Count > 0 && userDt.Rows[0]["group_id"] != DBNull.Value)
                {
                    studentGroupId = userDt.Rows[0]["group_id"];
                }

                // 2. Filtramos las sesiones ÚNICAMENTE para el grupo al que pertenece el estudiante
                string query = @"SELECT s.id AS ""Id"", s.session_number AS ""SessionNumber"", s.topic AS ""Topic"", 
                                        s.date AS ""Date"", s.zoom_link AS ""ZoomLink"", g.name AS ""GroupName"", s.group_id AS ""GroupId""
                                 FROM sessions s
                                 LEFT JOIN groups g ON s.group_id = g.id
                                 WHERE s.group_id = @GroupId
                                 ORDER BY s.date DESC;";

                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@GroupId", studentGroupId) };
                sessionsTable = await _postgresRepository.ExecuteQueryAsync(query, parameters);
            }
            else
            {
                // Administradores y profesores ven todas las sesiones
                string query = @"SELECT s.id AS ""Id"", s.session_number AS ""SessionNumber"", s.topic AS ""Topic"", 
                                        s.date AS ""Date"", s.zoom_link AS ""ZoomLink"", g.name AS ""GroupName"", s.group_id AS ""GroupId""
                                 FROM sessions s
                                 LEFT JOIN groups g ON s.group_id = g.id
                                 ORDER BY s.date DESC;";
                sessionsTable = await _postgresRepository.ExecuteQueryAsync(query);

                // Cargar la lista de grupos activos para el menú desplegable
                string groupsQuery = "SELECT id, name, country FROM groups WHERE is_active = true ORDER BY name ASC;";
                ViewBag.GroupsList = await _postgresRepository.ExecuteQueryAsync(groupsQuery);
            }

            ViewBag.SessionsList = sessionsTable;
            return View(sessionsTable);
        }

        // Crear o actualizar una sesión (Solo Admin y Teacher)
        [HttpPost]
        [Authorize(Roles = "admin,teacher")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertSession(Guid? id, int sessionNumber, Guid groupId, DateTime date, string topic, string? zoomLink)
        {
            string userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (userRole == "student") return Forbid();

            string query;
            NpgsqlParameter[] parameters;

            if (id.HasValue && id.Value != Guid.Empty)
            {
                // Actualizar sesión existente
                query = @"UPDATE sessions 
                          SET session_number = @SessionNumber, group_id = @GroupId, date = @Date, topic = @Topic, zoom_link = @ZoomLink 
                          WHERE id = @Id;";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@SessionNumber", sessionNumber),
                    new NpgsqlParameter("@GroupId", groupId),
                    new NpgsqlParameter("@Date", date),
                    new NpgsqlParameter("@Topic", topic),
                    new NpgsqlParameter("@ZoomLink", (object?)zoomLink ?? DBNull.Value)
                };
            }
            else
            {
                // Insertar nueva sesión
                query = @"INSERT INTO sessions (session_number, group_id, date, topic, zoom_link) 
                          VALUES (@SessionNumber, @GroupId, @Date, @Topic, @ZoomLink);";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@SessionNumber", sessionNumber),
                    new NpgsqlParameter("@GroupId", groupId),
                    new NpgsqlParameter("@Date", date),
                    new NpgsqlParameter("@Topic", topic),
                    new NpgsqlParameter("@ZoomLink", (object?)zoomLink ?? DBNull.Value)
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        // Eliminar sesión (Solo Admin y Teacher)
        [HttpPost]
        [Authorize(Roles = "admin,teacher")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSession(Guid id)
        {
            string userRole = User.FindFirst(ClaimTypes.Role)?.Value ?? "";
            if (userRole == "student") return Forbid();

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Id", id) };
            await _postgresRepository.ExecuteNonQueryAsync("DELETE FROM sessions WHERE id = @Id;", parameters);

            return RedirectToAction("Index");
        }
    }
}