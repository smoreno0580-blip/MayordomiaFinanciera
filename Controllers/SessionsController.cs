using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize(Roles = "admin,teacher,student")]
    public class SessionsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public SessionsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // Obtener sesiones con un JOIN opcional al nombre del grupo
            string sessionsQuery = @"SELECT s.*, g.name AS group_name 
                                     FROM sessions s 
                                     LEFT JOIN groups g ON s.group_id = g.id 
                                     ORDER BY s.date DESC";

            DataTable dtSessions = await _postgresRepository.ExecuteQueryAsync(sessionsQuery, null);
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups WHERE is_active = true ORDER BY name", null);

            ViewBag.GroupsList = dtGroups;
            return View(dtSessions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertSession(Guid? id, int sessionNumber, string topic, DateTime date, Guid groupId, string zoomLink)
        {
            string query = "";
            NpgsqlParameter[] parameters;

            if (id.HasValue && id.Value != Guid.Empty)
            {
                query = @"UPDATE sessions 
                          SET session_number = @SessionNumber, topic = @Topic, date = @Date, 
                              group_id = @GroupId, zoom_link = @ZoomLink 
                          WHERE id = @Id";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@SessionNumber", sessionNumber),
                    new NpgsqlParameter("@Topic", topic),
                    new NpgsqlParameter("@Date", date),
                    new NpgsqlParameter("@GroupId", groupId),
                    new NpgsqlParameter("@ZoomLink", (object?)zoomLink ?? DBNull.Value)
                };
            }
            else
            {
                query = @"INSERT INTO sessions (session_number, topic, date, group_id, zoom_link, created_at) 
                          VALUES (@SessionNumber, @Topic, @Date, @GroupId, @ZoomLink, NOW())";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@SessionNumber", sessionNumber),
                    new NpgsqlParameter("@Topic", topic),
                    new NpgsqlParameter("@Date", date),
                    new NpgsqlParameter("@GroupId", groupId),
                    new NpgsqlParameter("@ZoomLink", (object?)zoomLink ?? DBNull.Value)
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteSession(Guid id)
        {
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Id", id) };
            await _postgresRepository.ExecuteNonQueryAsync("DELETE FROM sessions WHERE id = @Id", parameters);
            return RedirectToAction("Index");
        }
    }
}