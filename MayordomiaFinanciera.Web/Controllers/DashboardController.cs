using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public DashboardController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Obtener tablas completas para calcular KPIs y métricas desde Supabase (PostgreSQL)
            DataTable dtUsers = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM users ORDER BY created_at DESC", null);
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups", null);
            DataTable dtSessions = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM sessions ORDER BY date DESC", null);
            DataTable dtPosts = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM posts ORDER BY created_at DESC", null);

            // 2. Calcular los KPIs principales
            int totalUsers = dtUsers != null ? dtUsers.Rows.Count : 0;
            int totalGroups = dtGroups != null ? dtGroups.Rows.Count : 0;
            int totalSessions = dtSessions != null ? dtSessions.Rows.Count : 0;

            // Contar cumpleaños del mes actual (usando el nombre de columna en minúsculas de PostgreSQL)
            int currentMonth = DateTime.Now.Month;
            int birthdaysCount = 0;
            if (dtUsers != null)
            {
                foreach (DataRow row in dtUsers.Rows)
                {
                    if (row["birth_date"] != DBNull.Value)
                    {
                        if (Convert.ToDateTime(row["birth_date"]).Month == currentMonth)
                        {
                            birthdaysCount++;
                        }
                    }
                }
            }

            // Pasar los valores a la vista mediante ViewBag
            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalGroups = totalGroups;
            ViewBag.TotalSessions = totalSessions;
            ViewBag.BirthdaysCount = birthdaysCount;

            // Pasar publicaciones recientes para el feed del dashboard
            ViewBag.RecentPosts = dtPosts;

            return View(dtUsers);
        }
    }
}