using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class BirthdaysController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public BirthdaysController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // Consulta directa a PostgreSQL para obtener los usuarios ordenados por nombre
            DataTable dt = await _postgresRepository.ExecuteQueryAsync(
                "SELECT u.*, g.name AS group_name FROM users u LEFT JOIN groups g ON u.group_id = g.id ORDER BY u.name ASC",
                null);

            return View(dt);
        }
    }
}