using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
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
            DataTable dt = await _postgresRepository.ExecuteQueryAsync(
                "SELECT * FROM posts ORDER BY created_at DESC",
                null);
            return View(dt);
        }

        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreatePost(string category, string content)
        {
            // Extraer el ID real desde la cookie de sesión del usuario actual
            string userIdStr = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? Guid.Empty.ToString();
            string userName = User.Identity?.Name ?? "Anónimo";

            Guid userId = Guid.Parse(userIdStr);

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@UserId", userId),
                new NpgsqlParameter("@Name", userName),
                new NpgsqlParameter("@Category", category),
                new NpgsqlParameter("@Content", content)
            };

            string query = @"INSERT INTO posts (user_id, name, category, content, created_at) 
                             VALUES (@UserId, @Name, @Category, @Content, NOW())";

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> ExportCsv()
        {
            DataTable dtPosts = await _postgresRepository.ExecuteQueryAsync(
                "SELECT * FROM posts ORDER BY created_at DESC",
                null);
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