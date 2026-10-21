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
        [Authorize(Roles = "admin,teacher")]
        public async Task<IActionResult> Index()
        {
            // Obtener Grupos ordenados por fecha de creación
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups ORDER BY created_at DESC", null);

            // Obtener Profesores para el selector
            DataTable dtTeachers = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM users WHERE role = 'teacher' ORDER BY name", null);

            ViewBag.TeachersList = dtTeachers;
            return View(dtGroups);
        }

        [HttpPost]
        [Authorize(Roles = "admin,teacher")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpsertGroup(Guid? id, string name, string country, string weekday, string time, string teachers, DateTime? startDate, DateTime? endDate)
        {
            string query = "";
            NpgsqlParameter[] parameters;

            // Determinar si es una actualización (si el ID tiene valor) o una inserción nueva
            if (id.HasValue && id.Value != Guid.Empty)
            {
                query = @"UPDATE groups 
                          SET name = @Name, country = @Country, weekday = @Weekday, time = @Time, 
                              teachers = @Teachers, start_date = @StartDate, end_date = @EndDate 
                          WHERE id = @Id";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@Name", name),
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
                query = @"INSERT INTO groups (name, country, weekday, time, is_active, teachers, start_date, end_date) 
                          VALUES (@Name, @Country, @Weekday, @Time, true, @Teachers, @StartDate, @EndDate)";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Name", name),
                    new NpgsqlParameter("@Country", country ?? "Panamá"),
                    new NpgsqlParameter("@Weekday", (object?)weekday ?? DBNull.Value),
                    new NpgsqlParameter("@Time", (object?)time ?? DBNull.Value),
                    new NpgsqlParameter("@Teachers", (object?)teachers ?? "General"),
                    new NpgsqlParameter("@StartDate", (object?)startDate ?? DBNull.Value),
                    new NpgsqlParameter("@EndDate", (object?)endDate ?? DBNull.Value)
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpPost]
        [Authorize(Roles = "admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteGroup(Guid id)
        {
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Id", id) };
            await _postgresRepository.ExecuteNonQueryAsync("DELETE FROM groups WHERE id = @Id", parameters);
            return RedirectToAction("Index");
        }
    }
}