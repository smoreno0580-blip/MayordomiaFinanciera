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
    public class DashboardController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public DashboardController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        public async Task<IActionResult> Index()
        {
            // 1. Obtener el ChurchId de los claims del usuario actual
            string? churchIdClaim = User.FindFirst("ChurchId")?.Value
                                    ?? User.FindFirst("church_id")?.Value
                                    ?? User.FindFirst("Church_Id")?.Value;

            Guid churchId = Guid.Empty;
            bool hasChurch = !string.IsNullOrEmpty(churchIdClaim) && Guid.TryParse(churchIdClaim, out churchId);

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value
                                    ?? User.FindFirst(ClaimTypes.Name)?.Value
                                    ?? User.Identity?.Name
                                    ?? "";

            // Variables para el rol eclesial de la iglesia
            string churchRole = string.Empty;

            // Si no se encontró church_id en los claims, intentar obtenerlo de la base de datos por su email o nombre
            if (!string.IsNullOrEmpty(userEmail))
            {
                string findUserQuery = "SELECT church_id, church_role FROM users WHERE (email = @Email OR name = @Email) LIMIT 1;";
                var userCheckParams = new NpgsqlParameter[] { new NpgsqlParameter("@Email", userEmail) };
                var dtUserCheck = await _postgresRepository.ExecuteQueryAsync(findUserQuery, userCheckParams);

                if (dtUserCheck.Rows.Count > 0)
                {
                    if (!hasChurch && dtUserCheck.Rows[0]["church_id"] != DBNull.Value)
                    {
                        if (Guid.TryParse(dtUserCheck.Rows[0]["church_id"].ToString(), out Guid dbChurchId))
                        {
                            churchId = dbChurchId;
                            hasChurch = true;
                        }
                    }

                    if (dtUserCheck.Rows[0]["church_role"] != DBNull.Value)
                    {
                        churchRole = dtUserCheck.Rows[0]["church_role"]?.ToString()?.ToUpper() ?? string.Empty;
                    }
                }
            }

            // 2. Obtener el nombre de la iglesia y datos de suscripción si el usuario está vinculado
            string churchName = string.Empty;
            string subscriptionStatus = "ACTIVE";
            DateTime? nextBillingDate = null;

            if (hasChurch && churchId != Guid.Empty)
            {
                string churchQuery = "SELECT name, subscription_status, next_billing_date FROM churches WHERE id = @ChurchId LIMIT 1;";
                var churchParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                var dtChurch = await _postgresRepository.ExecuteQueryAsync(churchQuery, churchParams);

                if (dtChurch.Rows.Count > 0)
                {
                    if (dtChurch.Rows[0]["name"] != DBNull.Value)
                    {
                        churchName = dtChurch.Rows[0]["name"]?.ToString() ?? string.Empty;
                    }
                    if (dtChurch.Rows[0]["subscription_status"] != DBNull.Value)
                    {
                        subscriptionStatus = dtChurch.Rows[0]["subscription_status"]?.ToString() ?? "ACTIVE";
                    }
                    if (dtChurch.Rows[0]["next_billing_date"] != DBNull.Value)
                    {
                        nextBillingDate = Convert.ToDateTime(dtChurch.Rows[0]["next_billing_date"]);
                    }
                }
            }

            // 3. Definir los parámetros y cláusulas de filtrado multi-tenant según el contexto del usuario
            string usersQuery;
            string groupsQuery;
            string sessionsQuery;
            string postsQuery;
            string bulletinsQuery;

            NpgsqlParameter[] userParams;
            NpgsqlParameter[] groupParams;
            NpgsqlParameter[] sessionParams;
            NpgsqlParameter[] postParams;
            NpgsqlParameter[] bulletinParams;

            if (hasChurch)
            {
                // Si pertenece a una iglesia, solo ve los datos de su propia congregación
                usersQuery = "SELECT * FROM users WHERE church_id = @ChurchId ORDER BY created_at DESC;";
                groupsQuery = "SELECT * FROM church_small_groups WHERE church_id = @ChurchId;";
                sessionsQuery = "SELECT * FROM sessions WHERE church_id = @ChurchId ORDER BY date DESC;";
                postsQuery = "SELECT * FROM posts WHERE church_id = @ChurchId ORDER BY created_at DESC;";
                bulletinsQuery = "SELECT id, title, content, publish_date FROM church_bulletins WHERE church_id = @ChurchId AND is_active = TRUE ORDER BY publish_date DESC LIMIT 5;";

                userParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                groupParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                sessionParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                postParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
                bulletinParams = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            }
            else
            {
                // Si es un usuario independiente (sin iglesia)
                usersQuery = "SELECT * FROM users WHERE church_id IS NULL ORDER BY created_at DESC;";
                groupsQuery = "SELECT * FROM groups WHERE church_id IS NULL;";
                sessionsQuery = "SELECT * FROM sessions WHERE church_id IS NULL ORDER BY date DESC;";
                postsQuery = "SELECT * FROM posts WHERE church_id IS NULL ORDER BY created_at DESC;";
                bulletinsQuery = "SELECT id, title, content, publish_date FROM bulletins WHERE church_id IS NULL AND is_published = TRUE ORDER BY publish_date DESC LIMIT 5;";

                userParams = Array.Empty<NpgsqlParameter>();
                groupParams = Array.Empty<NpgsqlParameter>();
                sessionParams = Array.Empty<NpgsqlParameter>();
                postParams = Array.Empty<NpgsqlParameter>();
                bulletinParams = Array.Empty<NpgsqlParameter>();
            }

            // 4. Ejecutar consultas filtradas contra PostgreSQL
            DataTable dtUsers = await _postgresRepository.ExecuteQueryAsync(usersQuery, userParams);
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync(groupsQuery, groupParams);
            DataTable dtSessions = await _postgresRepository.ExecuteQueryAsync(sessionsQuery, sessionParams);
            DataTable dtPosts = await _postgresRepository.ExecuteQueryAsync(postsQuery, postParams);
            DataTable dtBulletins = await _postgresRepository.ExecuteQueryAsync(bulletinsQuery, bulletinParams);

            // 5. Calcular los KPIs principales segmentados
            int totalUsers = dtUsers != null ? dtUsers.Rows.Count : 0;
            int totalGroups = dtGroups != null ? dtGroups.Rows.Count : 0;
            int totalSessions = dtSessions != null ? dtSessions.Rows.Count : 0;

            // Contar cumpleaños del mes actual para los miembros de este contexto
            int currentMonth = DateTime.Now.Month;
            int birthdaysCount = 0;
            if (dtUsers != null && dtUsers.Columns.Contains("birth_date"))
            {
                foreach (DataRow row in dtUsers.Rows)
                {
                    if (row["birth_date"] != DBNull.Value)
                    {
                        if (Convert.ToDateTime(row["birth_date"]).Month == currentMonth)
                        {
                            birthdaysCount++;
                        }
                    }
                }
            }

            // Pasar los valores, roles y datos de suscripción a la vista
            ViewBag.ChurchName = churchName;
            ViewBag.ChurchRole = churchRole;
            ViewBag.SubscriptionStatus = subscriptionStatus;
            ViewBag.NextBillingDate = nextBillingDate;
            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalGroups = totalGroups;
            ViewBag.TotalSessions = totalSessions;
            ViewBag.BirthdaysCount = birthdaysCount;

            // Pasar publicaciones y boletines recientes para el dashboard
            ViewBag.RecentPosts = dtPosts;
            ViewBag.ChurchBulletins = dtBulletins;

            return View(dtUsers);
        }
    }
}