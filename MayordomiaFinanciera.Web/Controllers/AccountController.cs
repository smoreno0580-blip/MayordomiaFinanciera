using MayordomiaFinanciera.Core.Interfaces; // Asegúrate de importar tu interfaz de correo
using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;
using System.Security.Claims;

namespace MayordomiaFinanciera.Web.Controllers
{
    public class AccountController : Controller
    {
        private readonly PostgresRepository _postgresRepository;
        private readonly IEmailService _emailService; // Servicio de correo inyectado

        public AccountController(PostgresRepository postgresRepository, IEmailService emailService)
        {
            _postgresRepository = postgresRepository;
            _emailService = emailService;
        }

        // Método auxiliar privado para cargar siempre los grupos activos
        private async Task LoadGroupsAsync()
        {
            try
            {
                string query = "SELECT id AS \"Id\", name AS \"Name\", country AS \"Country\" FROM groups WHERE is_active = true ORDER BY name ASC;";
                ViewBag.GroupsList = await _postgresRepository.ExecuteQueryAsync(query);
            }
            catch (Exception ex)
            {
                ViewBag.GroupsList = new DataTable();
                System.Diagnostics.Debug.WriteLine($"Error al cargar grupos: {ex.Message}");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Login()
        {
            await LoadGroupsAsync();
            return View("Login");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            await LoadGroupsAsync();
            return View("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(string email, string password)
        {
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Email", email),
                new NpgsqlParameter("@PasswordHash", password)
            };

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
            await LoadGroupsAsync();
            return View("Login");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction("Login", "Account");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Register(string name, string email, string phone, string password, DateTime birthDate, string country, string groupId)
        {
            try
            {
                // 1. Convertir el groupId recibido a un Guid de forma segura
                Guid? parsedGroupId = null;
                if (!string.IsNullOrEmpty(groupId) && Guid.TryParse(groupId, out Guid idVal))
                {
                    parsedGroupId = idVal;
                }

                // 2. Verificar si el correo ya existe
                string checkEmailQuery = "SELECT COUNT(1) FROM users WHERE email = @email;";
                var checkParams = new NpgsqlParameter[] { new NpgsqlParameter("@email", email ?? "") };
                DataTable emailCheckResult = await _postgresRepository.ExecuteQueryAsync(checkEmailQuery, checkParams);

                if (emailCheckResult.Rows.Count > 0 && Convert.ToInt32(emailCheckResult.Rows[0][0]) > 0)
                {
                    ModelState.AddModelError(string.Empty, "El correo electrónico ya se encuentra registrado.");
                    await LoadGroupsAsync();
                    return View("Login");
                }

                // 3. Insertar el usuario en la base de datos con su Guid correspondiente
                string insertSql = @"INSERT INTO users (name, email, phone, password_hash, birth_date, country, group_id, role) 
                             VALUES (@name, @email, @phone, @password, @birthDate, @country, @groupId, 'student');";

                var parameters = new NpgsqlParameter[]
                {
            new NpgsqlParameter("@name", name ?? (object)DBNull.Value),
            new NpgsqlParameter("@email", email ?? (object)DBNull.Value),
            new NpgsqlParameter("@phone", phone ?? (object)DBNull.Value),
            new NpgsqlParameter("@password", password ?? (object)DBNull.Value),
            new NpgsqlParameter("@birthDate", birthDate),
            new NpgsqlParameter("@country", country ?? (object)DBNull.Value),
            new NpgsqlParameter("@groupId", parsedGroupId.HasValue ? parsedGroupId.Value : (object)DBNull.Value)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertSql, parameters);

                // 4. Obtener el nombre real del grupo utilizando el Guid
                string groupName = "Sin grupo asignado";
                if (parsedGroupId.HasValue)
                {
                    string groupQuery = "SELECT name FROM groups WHERE id = @groupId;";
                    var groupParams = new NpgsqlParameter[] { new NpgsqlParameter("@groupId", parsedGroupId.Value) };
                    DataTable groupResult = await _postgresRepository.ExecuteQueryAsync(groupQuery, groupParams);
                    if (groupResult.Rows.Count > 0)
                    {
                        groupName = groupResult.Rows[0]["name"].ToString() ?? "Grupo General";
                    }
                }

                // 5. Enviar correo de confirmación con el nombre correcto del grupo
                try
                {
                    await _emailService.SendRegistrationConfirmationAsync(
                        toEmail: email,
                        fullName: name,
                        groupName: groupName,
                        username: email,
                        rawPassword: password
                    );
                }
                catch (Exception emailEx)
        {
                    System.Diagnostics.Debug.WriteLine($"Error al enviar correo de confirmación: {emailEx.Message}");
                }

                return RedirectToAction("Login", "Account");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError(string.Empty, $"Ocurrió un error al registrar: {ex.Message}");
                    await LoadGroupsAsync();
                    return View("Login");
                }
            }
    }
}