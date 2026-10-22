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
        #region GESTIÓN DE CONTRASEÑAS

        // 1. Cambio de contraseña por el propio usuario (desde la sesión activa)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
        {
            try
            {
                // Obtener el ID del usuario autenticado actual mediante los Claims de la Cookie
                string userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

                if (string.IsNullOrEmpty(userId))
                {
                    return Json(new { success = false, message = "Sesión no válida. Por favor inicie sesión nuevamente." });
                }

                if (newPassword != confirmPassword)
                {
                    return Json(new { success = false, message = "Las nuevas contraseñas no coinciden." });
                }

                if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
                {
                    return Json(new { success = false, message = "La nueva contraseña debe tener al menos 4 caracteres." });
                }

                // Consultar la contraseña actual en la base de datos
                string query = "SELECT password_hash FROM users WHERE id = @userId::uuid;";
                var param = new NpgsqlParameter("@userId", Guid.Parse(userId));
                var dt = await _postgresRepository.ExecuteQueryAsync(query, new NpgsqlParameter[] { param });

                if (dt.Rows.Count == 0 || dt.Rows[0]["password_hash"].ToString() != currentPassword)
                {
                    return Json(new { success = false, message = "La contraseña actual es incorrecta." });
                }

                // Actualizar con la nueva contraseña
                string updateSql = "UPDATE users SET password_hash = @newPassword WHERE id = @userId::uuid;";
                var updateParams = new NpgsqlParameter[]
                {
            new NpgsqlParameter("@newPassword", newPassword),
            new NpgsqlParameter("@userId", Guid.Parse(userId))
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateSql, updateParams);

                return Json(new { success = true, message = "Contraseña actualizada exitosamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Error al cambiar la contraseña: " + ex.Message });
            }
        }

        // 2. Solicitud de recuperación de contraseña ("¿Olvidaste tu contraseña?")
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ForgotPassword(string email)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(email))
                {
                    return Json(new { success = false, message = "Por favor ingrese un correo válido." });
                }

                // Buscar el usuario por su correo
                string findUserSql = "SELECT id, name, email FROM users WHERE LOWER(TRIM(email)) = LOWER(TRIM(@email));";
                var searchParams = new NpgsqlParameter[] { new NpgsqlParameter("@email", email.Trim()) };
                DataTable result = await _postgresRepository.ExecuteQueryAsync(findUserSql, searchParams);

                if (result.Rows.Count == 0)
                {
                    // Mensaje genérico por privacidad de datos
                    return Json(new { success = true, message = "Si el correo existe en el sistema, recibirás una clave temporal." });
                }

                string userId = result.Rows[0]["id"].ToString();
                string userName = result.Rows[0]["name"].ToString();
                string userEmail = result.Rows[0]["email"].ToString();

                // Generar clave temporal alfanumérica de 8 caracteres
                string tempPassword = Guid.NewGuid().ToString().Substring(0, 8);

                // Guardar la clave temporal en la BD
                string updateSql = "UPDATE users SET password_hash = @tempPass WHERE id = @userId::uuid;";
                var updateParams = new NpgsqlParameter[]
                {
                new NpgsqlParameter("@tempPass", tempPassword),
                new NpgsqlParameter("@userId", Guid.Parse(userId))
                };
                await _postgresRepository.ExecuteNonQueryAsync(updateSql, updateParams);

                // Enviar correo electrónico con la contraseña temporal
                try
                {
                    string subject = "Restablecimiento de Contraseña - Mayordomía Financiera";
                    string bodyHtml = $@"
                    <h2>Estimado/a {userName},</h2>
                    <p>Hemos recibido una solicitud para restablecer la contraseña de tu cuenta en <strong>Mayordomía Financiera</strong>.</p>
                    <p>Tu clave temporal de acceso es:</p>
                    <p style='font-size: 20px; font-weight: bold; background-color: #1B365D; color: #FFFFFF; padding: 12px; border-radius: 6px; display: inline-block;'>{tempPassword}</p>
                    <p>Por favor inicia sesión con esta clave y cámbiala de inmediato desde tu perfil.</p>
                    <br>
                    <p>Atentamente,<br><strong>Mayordomía Financiera Panamá</strong><br><a href='https://mayordomiayfinanzas.com'>mayordomiayfinanzas.com</a></p>";

                    await _emailService.SendEmailAsync(userEmail, subject, bodyHtml);
                }
                catch (Exception emailEx)
                {
                    System.Diagnostics.Debug.WriteLine($"Error al enviar correo de reseteo: {emailEx.Message}");
                }

                return Json(new { success = true, message = "Te hemos enviado una contraseña temporal a tu correo electrónico." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Ocurrió un error al procesar la solicitud: {ex.Message}" });
            }
        }

        // 3. Reseteo directo de contraseña por parte del Administrador (Exclusivo para rol 'admin')

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AdminResetUserPassword(string targetUserId, string newPassword)
        {
            try
            {
                // Validar que el usuario actual tenga el rol de administrador mediante la cookie de autenticación
                if (!User.IsInRole("admin"))
                {
                    return Json(new { success = false, message = "Acceso denegado. Solo administradores pueden realizar esta acción." });
                }

                if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 4)
                {
                    return Json(new { success = false, message = "La contraseña debe tener al menos 4 caracteres." });
                }

                // Actualizar la contraseña del usuario objetivo en la base de datos
                string updateSql = "UPDATE users SET password_hash = @newPassword WHERE id = @targetUserId::uuid;";
                var updateParams = new NpgsqlParameter[]
                {
            new NpgsqlParameter("@newPassword", newPassword),
            new NpgsqlParameter("@targetUserId", Guid.Parse(targetUserId))
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateSql, updateParams);

                return Json(new { success = true, message = "La contraseña ha sido restablecida correctamente." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Error al restablecer la contraseña: {ex.Message}" });
            }
        }

        #endregion
    }
}