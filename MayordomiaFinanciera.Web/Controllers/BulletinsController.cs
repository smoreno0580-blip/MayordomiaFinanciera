using MayordomiaFinanciera.Core.Interfaces;
using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class BulletinsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;
        private readonly IEmailService _emailService;

        public BulletinsController(PostgresRepository postgresRepository, IEmailService emailService)
        {
            _postgresRepository = postgresRepository;
            _emailService = emailService;
        }

        private Guid GetCurrentChurchId()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            if (!string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid churchId))
            {
                return churchId;
            }
            return Guid.Empty;
        }

        // 1. Listado de boletines (Público/Miembros ven activos, Pastores ven todos)
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            bool isPastorOrAdmin = User.IsInRole("admin") || User.IsInRole("ADMIN") ||
                                   User.IsInRole("PASTOR") || User.IsInRole("pastor") ||
                                   User.IsInRole("copastor") || User.IsInRole("COPASTOR");

            string query = isPastorOrAdmin
                ? "SELECT * FROM church_bulletins WHERE church_id = @ChurchId ORDER BY publish_date DESC;"
                : "SELECT * FROM church_bulletins WHERE church_id = @ChurchId AND is_active = true ORDER BY publish_date DESC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.IsPastorOrAdmin = isPastorOrAdmin;
            return View(dt);
        }

        // 2. [GET] Crear o Editar Boletín (Solo Administradores/Pastores)
        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,copastor,COPASTOR,lider,LIDER,teacher,TEACHER")]
        [HttpGet]
        public async Task<IActionResult> Upsert(Guid? id)
        {
            Guid churchId = GetCurrentChurchId();
            var model = new BulletinDto { ChurchId = churchId, PublishDate = DateTime.Today };

            if (id.HasValue && id.Value != Guid.Empty)
            {
                string query = "SELECT * FROM church_bulletins WHERE id = @Id AND church_id = @ChurchId;";
                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@ChurchId", churchId)
                };

                var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);
                if (dt.Rows.Count > 0)
                {
                    var row = dt.Rows[0];
                    model.Id = Guid.Parse(row["id"].ToString()!);
                    model.Title = row["title"]?.ToString() ?? "";
                    model.Content = row["content"]?.ToString() ?? "";
                    model.PublishDate = row["publish_date"] != DBNull.Value ? Convert.ToDateTime(row["publish_date"]) : DateTime.Today;
                    model.IsActive = row["is_active"] == DBNull.Value || Convert.ToBoolean(row["is_active"]);
                }
                else
                {
                    return NotFound();
                }
            }

            return View(model);
        }

        // 3. [POST] Guardar Boletín y Notificar por Correo a la Membresía
        [Authorize(Roles = "admin,ADMIN,PASTOR,pastor,copastor,COPASTOR,lider,LIDER,teacher,TEACHER")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(BulletinDto model)
        {
            Guid churchId = GetCurrentChurchId();
            model.ChurchId = churchId;

            if (!ModelState.IsValid) return View(model);

            if (model.Id == Guid.Empty)
            {
                string insertQuery = @"
                    INSERT INTO church_bulletins (church_id, title, content, publish_date, is_active)
                    VALUES (@ChurchId, @Title, @Content, @PublishDate, @IsActive);";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Title", model.Title ?? ""),
                    new NpgsqlParameter("@Content", model.Content ?? ""),
                    new NpgsqlParameter("@PublishDate", model.PublishDate),
                    new NpgsqlParameter("@IsActive", model.IsActive)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);

                // 📩 NOTIFICAR A TODOS LOS MIEMBROS DE LA IGLESIA SI EL BOLETÍN ESTÁ ACTIVO
                if (model.IsActive)
                {
                    string churchName = await GetChurchNameAsync(churchId);
                    var members = await GetChurchMembersAsync(churchId);

                    // Consultar la configuración SMTP específica de la iglesia (si existe y está activa)
                    var churchSmtpConfig = await GetChurchEmailSettingsAsync(churchId);

                    string bulletinTitle = model.Title ?? "Nuevo Anuncio Oficial";
                    string bulletinContent = model.Content ?? "";

                    // Ejecutar el envío masivo en segundo plano (asíncrono)
                    _ = Task.Run(async () =>
                    {
                        foreach (var member in members)
                        {
                            try
                            {
                                // Si tu IEmailService acepta parámetros SMTP personalizados, los puedes inyectar aquí.
                                // De lo contrario, el servicio interno evaluará la tabla church_email_settings por cada churchId.
                                await _emailService.SendBulletinNotificationAsync(
                                    member.Email,
                                    member.FullName,
                                    churchName,
                                    bulletinTitle,
                                    bulletinContent
                                );
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"Error enviando correo a {member.Email}: {ex.Message}");
                            }
                        }
                    });

                    TempData["SuccessMessage"] = "Boletín publicado y notificado por correo a los miembros activos.";
                }
                else
                {
                    TempData["SuccessMessage"] = "Boletín borrador guardado exitosamente.";
                }
            }
            else
            {
                string updateQuery = @"
                    UPDATE church_bulletins 
                    SET title = @Title, content = @Content, publish_date = @PublishDate, is_active = @IsActive
                    WHERE id = @Id AND church_id = @ChurchId;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", model.Id),
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@Title", model.Title ?? ""),
                    new NpgsqlParameter("@Content", model.Content ?? ""),
                    new NpgsqlParameter("@PublishDate", model.PublishDate),
                    new NpgsqlParameter("@IsActive", model.IsActive)
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
                TempData["SuccessMessage"] = "Boletín actualizado exitosamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        #region Métodos Auxiliares de Consulta

        private async Task<string> GetChurchNameAsync(Guid churchId)
        {
            string query = "SELECT name FROM churches WHERE id = @ChurchId;";
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            if (dt.Rows.Count > 0)
            {
                return dt.Rows[0]["name"]?.ToString() ?? "Nuestra Iglesia";
            }
            return "Nuestra Iglesia";
        }

        private async Task<List<(string Email, string FullName)>> GetChurchMembersAsync(Guid churchId)
        {
            var members = new List<(string Email, string FullName)>();

            string query = @"
                SELECT email, full_name 
                FROM users 
                WHERE church_id = @ChurchId 
                  AND is_active = true 
                  AND email IS NOT NULL 
                  AND email != '';";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            foreach (DataRow row in dt.Rows)
            {
                string email = row["email"]?.ToString() ?? "";
                string fullName = row["full_name"]?.ToString() ?? "Estimado(a) Miembro";

                if (!string.IsNullOrWhiteSpace(email))
                {
                    members.Add((email, fullName));
                }
            }

            return members;
        }

        // 🔍 Método para verificar si la iglesia tiene configuración SMTP personalizada en la nueva tabla
        private async Task<DataRow?> GetChurchEmailSettingsAsync(Guid churchId)
        {
            string query = @"
                SELECT smtp_host, smtp_port, smtp_user, smtp_password, smtp_sender_email, smtp_sender_name, use_custom_smtp
                FROM church_email_settings 
                WHERE church_id = @ChurchId AND use_custom_smtp = true 
                LIMIT 1;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            if (dt.Rows.Count > 0)
            {
                return dt.Rows[0];
            }
            return null; // Si no existe o está inactivo, retornará null para que el servicio use la cuenta general
        }

        #endregion
    }
}