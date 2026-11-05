using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;
using System.Security.Claims;

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
            // 1. Obtener el ChurchId de los claims de la sesión actual
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty; // <-- Inicializar aquí evita el error CS0165
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string query;
            NpgsqlParameter[]? parameters;

            if (hasChurch)
            {
                // Filtrar estrictamente por los usuarios de la iglesia del pastor/miembro
                query = @"
                    SELECT u.*, g.name AS group_name 
                    FROM users u 
                    LEFT JOIN groups g ON u.group_id = g.id 
                    WHERE u.church_id = @ChurchId 
                    ORDER BY u.name ASC;";

                parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                // Si es usuario independiente, mostrar solo usuarios sin iglesia asignada
                query = @"
                    SELECT u.*, g.name AS group_name 
                    FROM users u 
                    LEFT JOIN groups g ON u.group_id = g.id 
                    WHERE u.church_id IS NULL 
                    ORDER BY u.name ASC;";

                parameters = null;
            }

            // Consulta directa a PostgreSQL con el filtro multi-tenant aplicado
            DataTable dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            return View(dt);
        }
    }
}