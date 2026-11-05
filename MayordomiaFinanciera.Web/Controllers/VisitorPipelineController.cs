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
    public class VisitorPipelineController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public VisitorPipelineController(PostgresRepository postgresRepository)
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

        // 1. Tablero principal del Pipeline de Visitantes
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty)
            {
                return RedirectToAction("Index", "Home");
            }

            string query = "SELECT * FROM church_visitor_pipeline WHERE church_id = @ChurchId ORDER BY visit_date DESC;";
            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            return View(dt);
        }

        // 2. [GET] Registrar o Editar Visitante
        [HttpGet]
        public async Task<IActionResult> Upsert(Guid? id)
        {
            Guid churchId = GetCurrentChurchId();
            var model = new VisitorPipelineDto { ChurchId = churchId, VisitDate = DateTime.Today };

            if (id.HasValue && id.Value != Guid.Empty)
            {
                string query = "SELECT * FROM church_visitor_pipeline WHERE id = @Id AND church_id = @ChurchId;";
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
                    model.FullName = row["full_name"]?.ToString() ?? "";
                    model.Phone = row["phone"]?.ToString();
                    model.Email = row["email"]?.ToString();
                    model.Stage = row["stage"]?.ToString() ?? "Bienvenida";
                    model.AssignedTo = row["assigned_to"]?.ToString();
                    model.Notes = row["notes"]?.ToString();
                    model.VisitDate = row["visit_date"] != DBNull.Value ? Convert.ToDateTime(row["visit_date"]) : DateTime.Today;
                }
                else
                {
                    return NotFound();
                }
            }

            return View(model);
        }

        // 3. [POST] Guardar Visitante
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Upsert(VisitorPipelineDto model)
        {
            Guid churchId = GetCurrentChurchId();
            model.ChurchId = churchId;

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            if (model.Id == Guid.Empty)
            {
                string insertQuery = @"
                    INSERT INTO church_visitor_pipeline (church_id, full_name, phone, email, stage, assigned_to, notes, visit_date)
                    VALUES (@ChurchId, @FullName, @Phone, @Email, @Stage, @AssignedTo, @Notes, @VisitDate);";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@FullName", model.FullName ?? ""),
                    new NpgsqlParameter("@Phone", (object?)model.Phone ?? DBNull.Value),
                    new NpgsqlParameter("@Email", (object?)model.Email ?? DBNull.Value),
                    new NpgsqlParameter("@Stage", model.Stage ?? "Bienvenida"),
                    new NpgsqlParameter("@AssignedTo", (object?)model.AssignedTo ?? DBNull.Value),
                    new NpgsqlParameter("@Notes", (object?)model.Notes ?? DBNull.Value),
                    new NpgsqlParameter("@VisitDate", model.VisitDate)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);
                TempData["SuccessMessage"] = "Nuevo visitante registrado en la ruta de crecimiento.";
            }
            else
            {
                string updateQuery = @"
                    UPDATE church_visitor_pipeline 
                    SET full_name = @FullName, phone = @Phone, email = @Email, 
                        stage = @Stage, assigned_to = @AssignedTo, notes = @Notes, visit_date = @VisitDate
                    WHERE id = @Id AND church_id = @ChurchId;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", model.Id),
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@FullName", model.FullName ?? ""),
                    new NpgsqlParameter("@Phone", (object?)model.Phone ?? DBNull.Value),
                    new NpgsqlParameter("@Email", (object?)model.Email ?? DBNull.Value),
                    new NpgsqlParameter("@Stage", model.Stage ?? "Bienvenida"),
                    new NpgsqlParameter("@AssignedTo", (object?)model.AssignedTo ?? DBNull.Value),
                    new NpgsqlParameter("@Notes", (object?)model.Notes ?? DBNull.Value),
                    new NpgsqlParameter("@VisitDate", model.VisitDate)
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
                TempData["SuccessMessage"] = "Progreso del visitante actualizado correctamente.";
            }

            return RedirectToAction(nameof(Index));
        }

        // 4. Acción rápida para mover de etapa (Cambio dinámico)
        [HttpPost]
        public async Task<IActionResult> UpdateStage(Guid id, string stage)
        {
            Guid churchId = GetCurrentChurchId();
            string query = "UPDATE church_visitor_pipeline SET stage = @Stage WHERE id = @Id AND church_id = @ChurchId;";
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Stage", stage),
                new NpgsqlParameter("@Id", id),
                new NpgsqlParameter("@ChurchId", churchId)
            };

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return Ok();
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index");

            // Cambiamos 'status' por 'stage'
            string query = @"
        SELECT full_name, 
               COALESCE(phone, '') AS phone, 
               COALESCE(email, '') AS email, 
               COALESCE(stage, 'VISITOR') AS stage, 
               COALESCE(notes, '') AS notes, 
               visit_date 
        FROM church_visitor_pipeline 
        WHERE church_id = @ChurchId 
        ORDER BY visit_date DESC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("Nombre Completo,Telefono,Correo,Etapa Seguimiento,Notas,Fecha Visita");

            foreach (DataRow row in dt.Rows)
            {
                string name = $"\"{row["full_name"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string phone = $"\"{row["phone"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string email = $"\"{row["email"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string stage = $"\"{row["stage"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string notes = $"\"{row["notes"]?.ToString()?.Replace("\"", "\"\"")}\"";
                string date = row["visit_date"] != DBNull.Value
                    ? Convert.ToDateTime(row["visit_date"]).ToString("yyyy-MM-dd")
                    : "";

                builder.AppendLine($"{name},{phone},{email},{stage},{notes},{date}");
            }

            byte[] fileBytes = System.Text.Encoding.UTF8.GetPreamble()
                .Concat(System.Text.Encoding.UTF8.GetBytes(builder.ToString())).ToArray();

            return File(fileBytes, "text/csv", $"Seguimiento_Visitantes_{DateTime.Now:yyyyMMdd}.csv");
        }
    }
}