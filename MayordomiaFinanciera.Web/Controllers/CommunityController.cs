using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;
using System.Text;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class CommunityController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public CommunityController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // Obtener el ChurchId de los claims de la sesión actual
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty; // <-- Inicializar con Guid.Empty
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string query;
            NpgsqlParameter[]? parameters;

            if (hasChurch)
            {
                query = "SELECT * FROM posts WHERE church_id = @ChurchId ORDER BY created_at DESC;";
                parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                query = "SELECT * FROM posts WHERE church_id IS NULL ORDER BY created_at DESC;";
                parameters = null;
            }

            DataTable dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);
            return View(dt);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePost(string category, string content)
        {
            string userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString();
            string userName = User.Identity?.Name ?? "Anónimo";
            Guid userId = Guid.Parse(userIdStr);

            // 1. Intentar obtener el ChurchId de los claims de la sesión
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid? churchId = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out Guid cId) ? cId : (Guid?)null;

            // 2. RESPALDO DE SEGURIDAD: Si no está en el claim, buscarlo directamente en la tabla users
            if (!churchId.HasValue || churchId.Value == Guid.Empty)
            {
                var userQuery = "SELECT church_id FROM users WHERE id = @UserId;";
                var userParams = new NpgsqlParameter[] { new NpgsqlParameter("@UserId", userId) };
                DataTable userDt = await _postgresRepository.ExecuteQueryAsync(userQuery, userParams);

                if (userDt.Rows.Count > 0 && userDt.Rows[0]["church_id"] != DBNull.Value)
                {
                    churchId = Guid.Parse(userDt.Rows[0]["church_id"].ToString()!);
                }
            }

            var parameters = new NpgsqlParameter[]
            {
        new NpgsqlParameter("@UserId", userId),
        new NpgsqlParameter("@Name", userName),
        new NpgsqlParameter("@Category", category ?? "general"),
        new NpgsqlParameter("@Content", content ?? ""),
        new NpgsqlParameter("@ChurchId", churchId.HasValue ? (object)churchId.Value : DBNull.Value)
            };

            string query = @"INSERT INTO posts (user_id, name, category, content, created_at, church_id) 
                     VALUES (@UserId, @Name, @Category, @Content, NOW(), @ChurchId)";

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value ?? User.FindFirst("church_id")?.Value;
            Guid churchId = Guid.Empty; // <-- Inicializar con Guid.Empty
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string query;
            NpgsqlParameter[]? parameters;

            if (hasChurch)
            {
                query = "SELECT * FROM posts WHERE church_id = @ChurchId ORDER BY created_at DESC;";
                parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                query = "SELECT * FROM posts WHERE church_id IS NULL ORDER BY created_at DESC;";
                parameters = null;
            }

            DataTable dtPosts = await _postgresRepository.ExecuteQueryAsync(query, parameters);
            var sb = new StringBuilder();

            sb.Append('\ufeff');
            sb.AppendLine("ID,Autor,Categoría,Contenido,Fecha de Creación");

            foreach (DataRow row in dtPosts.Rows)
            {
                string id = row["id"]?.ToString() ?? "";
                string author = row["name"]?.ToString()?.Replace("\"", "\"\"") ?? "Anónimo";
                string category = row["category"]?.ToString() ?? "general";
                string content = row["content"]?.ToString()?.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") ?? "";
                string createdAt = row["created_at"] != DBNull.Value ? Convert.ToDateTime(row["created_at"]).ToString("yyyy-MM-dd HH:mm") : "";

                sb.AppendLine($"\"{id}\",\"{author}\",\"{category}\",\"{content}\",\"{createdAt}\"");
            }

            byte[] buffer = Encoding.UTF8.GetBytes(sb.ToString());
            string fileName = $"Comunidad_Muro_Compass_{DateTime.Now:yyyy-MM-dd}.csv";
            return File(buffer, "text/csv", fileName);
        }
    }
}