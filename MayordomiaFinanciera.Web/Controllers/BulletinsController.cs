using MayordomiaFinanciera.Infrastructure.Repositories;
using MayordomiaFinanciera.Web.Models.Church;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Data;
using System.Threading.Tasks;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class BulletinsController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public BulletinsController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
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

        // 3. [POST] Guardar Boletín
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
                TempData["SuccessMessage"] = "Boletín publicado exitosamente.";
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
    }
}