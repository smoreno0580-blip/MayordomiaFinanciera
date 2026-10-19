using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using MayordomiaFinanciera.Infrastructure.Repositories;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class DonationsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public DonationsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterDonation(decimal amount, string paymentMethod)
        {
            // Extraer el ID real del usuario desde los claims de la sesión si está disponible
            string userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Guid? userId = !string.IsNullOrEmpty(userIdStr) ? Guid.Parse(userIdStr) : null;
            var userName = User.Identity?.Name ?? "Anónimo";

            // Parámetros adaptados para la función PL/pgSQL sp_save_donation en Supabase
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@p_user_id", (object?)userId ?? DBNull.Value),
                new NpgsqlParameter("@p_donor_name", userName),
                new NpgsqlParameter("@p_amount", amount),
                new NpgsqlParameter("@p_payment_method", paymentMethod)
            };

            // Invocación del procedimiento/función almacenada en PostgreSQL
            await _postgresRepository.ExecuteNonQueryAsync("SELECT sp_save_donation(@p_user_id, @p_donor_name, @p_amount, @p_payment_method)", parameters);

            TempData["Success"] = "¡Muchas gracias por tu generosidad! Tu donación ha sido registrada con éxito.";

            return RedirectToAction("Index");
        }
    }
}