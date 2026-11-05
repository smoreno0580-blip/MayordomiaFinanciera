using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Data;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class SubscriptionController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public SubscriptionController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        // Pantalla de bloqueo mostrada cuando la suscripción está vencida (Accesible para la iglesia afectada)
        [HttpGet]
        public IActionResult PendingPayment()
        {
            return View();
        }

        // Listado global de iglesias y estados de cobro mensual (EXCLUSIVO para SuperAdmin / Administrador de la plataforma)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            // Validar si el usuario en sesión es Administrador General de la plataforma
            bool isPlatformAdmin = User.IsInRole("ADMIN") || User.IsInRole("admin") ||
                                   User.IsInRole("SuperAdmin") || User.IsInRole("SUPERADMIN") ||
                                   User.Identity.Name?.Contains("admin") == true;

            if (!isPlatformAdmin)
            {
                // Si no es el administrador de la plataforma, denegar el acceso y redirigir al dashboard
                TempData["ErrorMessage"] = "No tienes permisos para acceder al panel general de suscripciones.";
                return RedirectToAction("Index", "Dashboard");
            }

            string query = @"SELECT id, name, subscription_status, next_billing_date, last_payment_date, monthly_fee FROM churches ORDER BY name ASC;";
            var dt = await _postgresRepository.ExecuteQueryAsync(query, Array.Empty<NpgsqlParameter>());
            return View(dt);
        }

        // Registrar pago mensual y extender la vigencia (EXCLUSIVO para SuperAdmin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RegisterPayment(Guid churchId)
        {
            if (!IsPlatformAdminUser())
            {
                return Unauthorized();
            }

            string query = @"
                UPDATE churches 
                SET subscription_status = 'ACTIVE',
                    last_payment_date = NOW(),
                    next_billing_date = NOW() + INTERVAL '30 days'
                WHERE id = @ChurchId;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);

            TempData["SuccessMessage"] = "Pago mensual registrado con éxito. Suscripción extendida por 30 días.";
            return RedirectToAction(nameof(Index));
        }

        // Suspender manualmente el acceso por impago (EXCLUSIVO para SuperAdmin)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuspendSubscription(Guid churchId)
        {
            if (!IsPlatformAdminUser())
            {
                return Unauthorized();
            }

            string query = "UPDATE churches SET subscription_status = 'SUSPENDED' WHERE id = @ChurchId;";
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);

            TempData["ErrorMessage"] = "La suscripción de la iglesia ha sido suspendida y su acceso inhabilitado.";
            return RedirectToAction(nameof(Index));
        }

        // Exportar la lista de suscripciones a CSV (EXCLUSIVO para SuperAdmin)
        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            if (!IsPlatformAdminUser())
            {
                TempData["ErrorMessage"] = "No tienes permisos para realizar esta exportación.";
                return RedirectToAction("Index", "Dashboard");
            }

            string query = @"SELECT id, name, subscription_status, next_billing_date, last_payment_date, monthly_fee FROM churches ORDER BY name ASC;";
            var dt = await _postgresRepository.ExecuteQueryAsync(query, Array.Empty<NpgsqlParameter>());

            var csvBuilder = new System.Text.StringBuilder();

            // Encabezados del CSV
            csvBuilder.AppendLine("ID,Iglesia / Ministerio,Estado de Suscripción,Último Pago,Próximo Corte,Cuota Mensual");

            foreach (System.Data.DataRow row in dt.Rows)
            {
                string id = row["id"]?.ToString() ?? "";
                string name = $"\"{row["name"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string status = row["subscription_status"]?.ToString() ?? "";
                string lastPayment = row["last_payment_date"] != DBNull.Value ? Convert.ToDateTime(row["last_payment_date"]).ToString("yyyy-MM-dd") : "N/A";
                string nextBilling = row["next_billing_date"] != DBNull.Value ? Convert.ToDateTime(row["next_billing_date"]).ToString("yyyy-MM-dd") : "N/A";
                string fee = row["monthly_fee"]?.ToString() ?? "0.00";

                csvBuilder.AppendLine($"{id},{name},{status},{lastPayment},{nextBilling},{fee}");
            }

            byte[] buffer = System.Text.Encoding.UTF8.GetBytes(csvBuilder.ToString());
            string fileName = $"Suscripciones_Iglesias_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(buffer, "text/csv", fileName);
        }

        // Método auxiliar privado para validar permisos de administrador de la plataforma
        private bool IsPlatformAdminUser()
        {
            return User.IsInRole("ADMIN") || User.IsInRole("admin") ||
                   User.IsInRole("SuperAdmin") || User.IsInRole("SUPERADMIN") ||
                   User.Identity.Name?.Contains("admin") == true;
        }
    }
}