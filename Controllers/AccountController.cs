using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes;
using System.Data;
using System.Security.Claims;

namespace MayordomiaFinanciera.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public AccountController(PostgresRepository postgresRepository)
        {
            _postgresRepository = postgresRepository;
        }

        [HttpGet]
        public IActionResult Login()
        {
            return View(); // Esto renderiza Login.cshtml
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            // Cambiado a NpgsqlParameter para PostgreSQL / Supabase
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Email", email),
                new NpgsqlParameter("@PasswordHash", password)
            };

            // Llamada a la función o consulta SQL compatible con Postgres
            DataTable result = await _postgresRepository.ExecuteQueryAsync(
                "SELECT * FROM users WHERE email = @Email AND password_hash = @PasswordHash",
                parameters);

            if (result.Rows.Count > 0)
            {
                var row = result.Rows[0];
                string userId = row["id"].ToString()!;
                string userName = row["name"].ToString()!;
                string userRole = row["role"].ToString()!;

                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, userId),
                    new Claim(ClaimTypes.Name, userName),
                    new Claim(ClaimTypes.Email, email),
                    new Claim(ClaimTypes.Role, userRole)
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);

                await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(claimsIdentity));

                return RedirectToAction("Index", "Dashboard");
            }

            ModelState.AddModelError(string.Empty, "Correo o contraseña incorrectos.");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [HttpGet]
        public async Task<IActionResult> Register()
        {
            DataTable dtGroups = await _postgresRepository.ExecuteQueryAsync("SELECT * FROM groups WHERE is_active = true", null);
            ViewBag.GroupsList = dtGroups;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string name, string email, string password, string phone, string country, DateTime? birthDate, Guid? groupId)
        {
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Email", email),
                new NpgsqlParameter("@PasswordHash", password),
                new NpgsqlParameter("@Name", name),
                new NpgsqlParameter("@Phone", (object?)phone ?? DBNull.Value),
                new NpgsqlParameter("@Role", "student"),
                new NpgsqlParameter("@Country", country ?? "Panamá"),
                new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                new NpgsqlParameter("@GroupId", (object?)groupId ?? DBNull.Value)
            };

            string query = @"INSERT INTO users (email, password_hash, name, phone, role, country, birth_date, group_id) 
                             VALUES (@Email, @PasswordHash, @Name, @Phone, @Role, @Country, @BirthDate, @GroupId)";

            await _postgresRepository.ExecuteNonQueryAsync(query, parameters);

            TempData["SuccessMessage"] = "Registro exitoso. Por favor, inicia sesión.";
            return RedirectToAction("Login");
        }
    }
}