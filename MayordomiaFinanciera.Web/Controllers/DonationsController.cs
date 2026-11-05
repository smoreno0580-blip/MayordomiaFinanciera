using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Data;
using System.Security.Claims;
using System.Threading.Tasks;

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

        // 1. Vista pública/miembro de Donaciones (Filtra por la iglesia del usuario o muestra el general)
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty;
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            DataTable dt = new DataTable();
            dt.Columns.Add("payment_method", typeof(string));
            dt.Columns.Add("title", typeof(string));
            dt.Columns.Add("details", typeof(string));

            bool churchHasData = false;

            if (hasChurch)
            {
                // CORREGIDO: Apunta a la tabla real 'church_payment_settings' que se ve en Supabase
                string queryChurch = "SELECT * FROM church_payment_settings WHERE church_id = @ChurchId;";
                var parametersChurch = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                var dtChurch = await _postgresRepository.ExecuteQueryAsync(queryChurch, parametersChurch);

                if (dtChurch.Rows.Count > 0)
                {
                    var row = dtChurch.Rows[0];
                    churchHasData = true;

                    // Mapear Transferencia Bancaria (ACH) si está habilitada
                    bool isAchEnabled = row["is_ach_enabled"] != DBNull.Value && Convert.ToBoolean(row["is_ach_enabled"]);
                    if (isAchEnabled)
                    {
                        string bank = row["ach_bank_name"]?.ToString() ?? "";
                        string type = row["ach_account_type"]?.ToString() ?? "";
                        string number = row["ach_account_number"]?.ToString() ?? "";
                        string holder = row["ach_account_holder"]?.ToString() ?? "";

                        dt.Rows.Add("bank", "Transferencia Bancaria", $"{bank} - {type}: {number}\nA nombre de: {holder}");
                    }

                    // Mapear Yappy si está habilitado
                    bool isYappyEnabled = row["is_yappy_enabled"] != DBNull.Value && Convert.ToBoolean(row["is_yappy_enabled"]);
                    if (isYappyEnabled)
                    {
                        string phone = row["yappy_phone"]?.ToString() ?? "";
                        string alias = row["yappy_alias"]?.ToString() ?? "";

                        dt.Rows.Add("yappy", "Yappy", $"Teléfono: {phone}\nAlias: {alias}");
                    }

                    // Mapear PayPal si está habilitado
                    bool isPaypalEnabled = row["is_paypal_enabled"] != DBNull.Value && Convert.ToBoolean(row["is_paypal_enabled"]);
                    if (isPaypalEnabled)
                    {
                        string paypal = row["paypal_email"]?.ToString() ?? "";
                        dt.Rows.Add("paypal", "Donación vía PayPal", $"Correo asociado: {paypal}");
                    }
                }
            }

            // 2. Si no tiene iglesia, o si la iglesia existe pero aún no tiene datos en esta tabla -> Respaldo General
            if (!hasChurch || !churchHasData)
            {
                // Opcional: Puedes usar 'global_payment_settings' o 'donation_settings' según tu estructura general
                string queryGlobal = "SELECT * FROM donation_settings WHERE church_id IS NULL AND is_active = true ORDER BY id;";
                dt = await _postgresRepository.ExecuteQueryAsync(queryGlobal, null);

                if (hasChurch)
                {
                    ViewBag.IsFallbackToGeneral = true;
                }
            }

            return View(dt);
        }

        // 2. Vista de Administración / Edición (Aislada por la iglesia del pastor o global para admin general)
        [Authorize(Roles = "admin,PASTOR,ADMIN")]
        [HttpGet]
        public async Task<IActionResult> Manage()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty;
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string query;
            NpgsqlParameter[]? parameters;

            if (hasChurch)
            {
                // El pastor solo administra los métodos de su propia iglesia
                query = "SELECT * FROM donation_settings WHERE church_id = @ChurchId ORDER BY id;";
                parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                // El administrador general ve los métodos globales del ministerio
                query = "SELECT * FROM donation_settings WHERE church_id IS NULL ORDER BY id;";
                parameters = null;
            }

            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);
            return View(dt);
        }

        // 3. Acción para actualizar o insertar la información de pago asegurando el scope del church_id
        [Authorize(Roles = "admin,PASTOR,ADMIN")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateMethod(int? id, string title, string details, bool isActive)
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid? churchId = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid cId) ? cId : (Guid?)null;

            string query;
            NpgsqlParameter[] parameters;

            if (id.HasValue && id.Value > 0)
            {
                // Actualizar registro existente (validando opcionalmente que pertenezca a su iglesia o sea global)
                query = @"UPDATE donation_settings 
                          SET title = @title, details = @details, is_active = @isActive, updated_at = CURRENT_TIMESTAMP 
                          WHERE id = @id;";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@id", id.Value),
                    new NpgsqlParameter("@title", title ?? ""),
                    new NpgsqlParameter("@details", details ?? ""),
                    new NpgsqlParameter("@isActive", isActive)
                };
            }
            else
            {
                // Insertar nuevo método ligado a la iglesia del pastor o general
                query = @"INSERT INTO donation_settings (church_id, title, details, is_active, updated_at) 
                          VALUES (@churchId, @title, @details, @isActive, CURRENT_TIMESTAMP);";

                parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@churchId", churchId.HasValue ? (object)churchId.Value : DBNull.Value),
                    new NpgsqlParameter("@title", title ?? ""),
                    new NpgsqlParameter("@details", details ?? ""),
                    new NpgsqlParameter("@isActive", isActive)
                };
            }

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            TempData["SuccessMessage"] = "Método de donación actualizado correctamente.";
            return RedirectToAction(nameof(Manage));
        }
    }
}