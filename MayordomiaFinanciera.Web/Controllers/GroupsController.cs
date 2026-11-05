using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;
using System.Security.Claims;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class GroupsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public GroupsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        [HttpGet]
        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,copastor,COPASTOR,lider,LIDER,teacher,TEACHER")]
        public async Task<IActionResult> Index()
        {
            // Obtener el ChurchId de los claims de la sesión actual
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty;
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string groupsQuery;
            string teachersQuery;
            NpgsqlParameter[]? groupParams;
            NpgsqlParameter[]? teacherParams;

            if (hasChurch)
            {
                groupsQuery = "SELECT * FROM groups WHERE church_id = @ChurchId ORDER BY created_at DESC;";
                teachersQuery = "SELECT * FROM users WHERE (LOWER(role) = 'teacher' OR LOWER(role) = 'pastor') AND church_id = @ChurchId ORDER BY name;";

                groupParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                teacherParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                groupsQuery = "SELECT * FROM groups WHERE church_id IS NULL ORDER BY created_at DESC;";
                teachersQuery = "SELECT * FROM users WHERE (LOWER(role) = 'teacher' OR LOWER(role) = 'pastor') AND church_id IS NULL ORDER BY name;";

                groupParams = null;
                teacherParams = null;
            }

            // Obtener Grupos y Profesores filtrados por contexto eclesial
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync(groupsQuery, groupParams);
            DataTable dtTeachers = await _postgresRepository.ExecuteQueryAsync(teachersQuery, teacherParams);

            ViewBag.TeachersList = dtTeachers;
            return View(dtGroups);
        }

        [HttpPost]
        [Authorize(Roles = "admin,teacher,TEACHER,PASTOR,ADMIN")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertGroup(Guid? id, string name, string country, string weekday, string time, string teachers, DateTime? startDate, DateTime? endDate)
        {
            // Obtener el ChurchId del usuario actual de forma segura como Nullable Guid
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid? parsedChurchId = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid cId) ? cId : (Guid?)null;

            string query = "";
            NpgsqlParameter[] parameters;

            // Determinar si es una actualización o una inserción nueva
            if (id.HasValue && id.Value != Guid.Empty)
            {
                query = @"UPDATE groups 
                          SET name = @Name, country = @Country, weekday = @Weekday, time = @Time, 
                              teachers = @Teachers, start_date = @StartDate, end_date = @EndDate 
                          WHERE id = @Id";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@Name", name ?? ""),
                    new NpgsqlParameter("@Country", country ?? "Panamá"),
                    new NpgsqlParameter("@Weekday", (object?)weekday ?? DBNull.Value),
                    new NpgsqlParameter("@Time", (object?)time ?? DBNull.Value),
                    new NpgsqlParameter("@Teachers", (object?)teachers ?? "General"),
                    new NpgsqlParameter("@StartDate", (object?)startDate ?? DBNull.Value),
                    new NpgsqlParameter("@EndDate", (object?)endDate ?? DBNull.Value)
                };
            }
            else
            {
                query = @"INSERT INTO groups (name, country, weekday, time, is_active, teachers, start_date, end_date, church_id) 
                          VALUES (@Name, @Country, @Weekday, @Time, true, @Teachers, @StartDate, @EndDate, @ChurchId)";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Name", name ?? ""),
                    new NpgsqlParameter("@Country", country ?? "Panamá"),
                    new NpgsqlParameter("@Weekday", (object?)weekday ?? DBNull.Value),
                    new NpgsqlParameter("@Time", (object?)time ?? DBNull.Value),
                    new NpgsqlParameter("@Teachers", (object?)teachers ?? "General"),
                    new NpgsqlParameter("@StartDate", (object?)startDate ?? DBNull.Value),
                    new NpgsqlParameter("@EndDate", (object?)endDate ?? DBNull.Value),
                    new NpgsqlParameter("@ChurchId", parsedChurchId.HasValue ? (object)parsedChurchId.Value : DBNull.Value)
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Roles = "admin,TEACHER,PASTOR,ADMIN")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGroup(Guid id)
        {
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Id", id) };
            await _postgresRepository.ExecuteNonQueryAsync("DELETE FROM groups WHERE id = @Id", parameters);
            return RedirectToAction("Index");
        }
    }
}