using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System.Data;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class SundaySchoolController : Controller
    {
        private readonly PostgresRepository _postgresRepository;

        public SundaySchoolController(PostgresRepository postgresRepository)
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

        // 🔐 Validación de permisos (Rol de Sistema + church_role en BD)
        private async Task<bool> UserCanManageSundaySchoolAsync()
        {
            if (User.IsInRole("admin") || User.IsInRole("ADMIN") ||
                User.IsInRole("pastor") || User.IsInRole("PASTOR") ||
                User.IsInRole("copastor") || User.IsInRole("COPASTOR"))
            {
                return true;
            }

            string userEmail = User.FindFirst(ClaimTypes.Email)?.Value
                               ?? User.FindFirst(ClaimTypes.Name)?.Value
                               ?? User.Identity?.Name ?? "";

            if (!string.IsNullOrEmpty(userEmail))
            {
                string query = "SELECT church_role FROM users WHERE (email = @Email OR name = @Email) LIMIT 1;";
                var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@Email", userEmail) };
                var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

                if (dt.Rows.Count > 0 && dt.Rows[0]["church_role"] != DBNull.Value)
                {
                    string churchRole = dt.Rows[0]["church_role"]?.ToString()?.ToUpper() ?? "";
                    if (churchRole.Contains("PASTOR") || churchRole.Contains("ADMIN") ||
                        churchRole.Contains("COPASTOR") || churchRole.Contains("LIDER") ||
                        churchRole.Contains("TEACHER") || churchRole.Contains("PROFESOR"))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        // 📋 1. Listado y Panel Principal de Check-In
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos de rol eclesial para acceder a la Escuela Dominical.";
                return RedirectToAction("Index", "Dashboard");
            }

            string query = @"
                SELECT c.id, c.first_name, c.last_name, c.birth_date, c.allergy_notes, 
                       c.parent_guardian_name, c.parent_phone, c.secure_code,
                       a.id AS attendance_id, a.check_in_time, a.status
                FROM children c
                LEFT JOIN sunday_school_attendance a 
                       ON c.id = a.child_id AND a.status = 'CHECKED_IN' AND DATE(a.check_in_time) = CURRENT_DATE
                WHERE c.church_id = @ChurchId
                ORDER BY c.first_name ASC;";

            var parameters = new NpgsqlParameter[] { new NpgsqlParameter("@ChurchId", churchId) };
            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.CurrentChurchId = churchId;
            return View(dt);
        }

        // ✅ 2. Realizar Check-In de un Niño
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckIn(Guid childId, string guardianName, string secureCode)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || childId == Guid.Empty) return RedirectToAction(nameof(Index));

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para realizar Check-In.";
                return RedirectToAction(nameof(Index));
            }

            string insertQuery = @"
                INSERT INTO sunday_school_attendance 
                (church_id, child_id, check_in_time, checked_in_by, security_code_used, status)
                VALUES 
                (@ChurchId, @ChildId, CURRENT_TIMESTAMP, @GuardianName, @SecureCode, 'CHECKED_IN');";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@ChildId", childId),
                new NpgsqlParameter("@GuardianName", (object?)guardianName ?? "Padre / Tutor"),
                new NpgsqlParameter("@SecureCode", (object?)secureCode ?? "0000")
            };

            await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);
            TempData["SuccessMessage"] = "¡Check-In realizado con éxito! Niño registrado en el aula.";

            return RedirectToAction(nameof(Index));
        }

        // 🚪 3. Realizar Check-Out (Retiro seguro validando PIN en servidor)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(Guid attendanceId, string returingGuardian, string securityCode)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || attendanceId == Guid.Empty) return RedirectToAction(nameof(Index));

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para realizar Check-Out.";
                return RedirectToAction(nameof(Index));
            }

            if (string.IsNullOrWhiteSpace(securityCode))
            {
                TempData["ErrorMessage"] = "Debe ingresar el PIN de seguridad para retirar al menor.";
                return RedirectToAction(nameof(Index));
            }

            // 1. Obtener el PIN esperado del registro activo y del niño
            string verifyQuery = @"
                SELECT COALESCE(NULLIF(TRIM(a.security_code_used), ''), TRIM(c.secure_code)) AS expected_pin,
                       c.first_name || ' ' || c.last_name AS child_name
                FROM sunday_school_attendance a
                INNER JOIN children c ON a.child_id = c.id
                WHERE a.id = @AttendanceId 
                  AND a.church_id = @ChurchId 
                  AND a.status = 'CHECKED_IN'
                LIMIT 1;";

            var verifyParams = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@AttendanceId", attendanceId),
                new NpgsqlParameter("@ChurchId", churchId)
            };

            var dtVerify = await _postgresRepository.ExecuteQueryAsync(verifyQuery, verifyParams);

            if (dtVerify.Rows.Count == 0)
            {
                TempData["ErrorMessage"] = "El registro de asistencia no está activo o ya fue retirado.";
                return RedirectToAction(nameof(Index));
            }

            string expectedPin = dtVerify.Rows[0]["expected_pin"]?.ToString()?.Trim() ?? "";
            string childName = dtVerify.Rows[0]["child_name"]?.ToString() ?? "el menor";

            // 2. Validar coincidencia exacta del PIN
            if (!string.Equals(expectedPin, securityCode.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = $"PIN incorrecto. No se autorizó el retiro de {childName}. Verifique el código con el tutor.";
                return RedirectToAction(nameof(Index));
            }

            // 3. Registrar Check-Out válido
            string updateQuery = @"
                UPDATE sunday_school_attendance
                SET check_out_time = CURRENT_TIMESTAMP, 
                    checked_out_by = @ReturingGuardian, 
                    status = 'CHECKED_OUT'
                WHERE id = @AttendanceId AND church_id = @ChurchId;";

            var updateParams = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@AttendanceId", attendanceId),
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@ReturingGuardian", string.IsNullOrWhiteSpace(returingGuardian) ? "Padre / Tutor" : returingGuardian.Trim())
            };

            await _postgresRepository.ExecuteNonQueryAsync(updateQuery, updateParams);
            TempData["SuccessMessage"] = $"¡Check-Out verificado! {childName} fue entregado de forma segura.";

            return RedirectToAction(nameof(Index));
        }

        // ➕ / ✏️ 4. Registrar o Actualizar Niño en el Sistema (Upsert)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveChild(
            Guid? id,
            string firstName,
            string lastName,
            DateTime? birthDate,
            string? allergyNotes,
            string parentName,
            string parentPhone,
            string? secureCode)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return Unauthorized();

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para modificar registros infantiles.";
                return RedirectToAction(nameof(Index));
            }

            if (!id.HasValue || id.Value == Guid.Empty)
            {
                string insertQuery = @"
                    INSERT INTO children 
                    (church_id, first_name, last_name, birth_date, allergy_notes, parent_guardian_name, parent_phone, secure_code)
                    VALUES 
                    (@ChurchId, @FirstName, @LastName, @BirthDate, @AllergyNotes, @ParentName, @ParentPhone, @SecureCode);";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@FirstName", firstName),
                    new NpgsqlParameter("@LastName", lastName),
                    new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                    new NpgsqlParameter("@AllergyNotes", (object?)allergyNotes ?? DBNull.Value),
                    new NpgsqlParameter("@ParentName", parentName),
                    new NpgsqlParameter("@ParentPhone", parentPhone),
                    new NpgsqlParameter("@SecureCode", string.IsNullOrWhiteSpace(secureCode) ? "1234" : secureCode)
                };

                await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);
                TempData["SuccessMessage"] = "¡Niño registrado correctamente en la base de datos de la Escuela Dominical!";
            }
            else
            {
                string updateQuery = @"
                    UPDATE children
                    SET first_name = @FirstName,
                        last_name = @LastName,
                        birth_date = @BirthDate,
                        allergy_notes = @AllergyNotes,
                        parent_guardian_name = @ParentName,
                        parent_phone = @ParentPhone,
                        secure_code = @SecureCode
                    WHERE id = @Id AND church_id = @ChurchId;";

                var parameters = new NpgsqlParameter[]
                {
                    new NpgsqlParameter("@Id", id.Value),
                    new NpgsqlParameter("@ChurchId", churchId),
                    new NpgsqlParameter("@FirstName", firstName),
                    new NpgsqlParameter("@LastName", lastName),
                    new NpgsqlParameter("@BirthDate", (object?)birthDate ?? DBNull.Value),
                    new NpgsqlParameter("@AllergyNotes", (object?)allergyNotes ?? DBNull.Value),
                    new NpgsqlParameter("@ParentName", parentName),
                    new NpgsqlParameter("@ParentPhone", parentPhone),
                    new NpgsqlParameter("@SecureCode", string.IsNullOrWhiteSpace(secureCode) ? "1234" : secureCode)
                };

                await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
                TempData["SuccessMessage"] = "¡Datos del niño actualizados correctamente!";
            }

            return RedirectToAction(nameof(Index));
        }

        // 🗑️ 5. Eliminar Registro de Niño
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteChild(Guid id)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || id == Guid.Empty) return RedirectToAction(nameof(Index));

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para eliminar registros infantiles.";
                return RedirectToAction(nameof(Index));
            }

            string deleteQuery = "DELETE FROM children WHERE id = @Id AND church_id = @ChurchId;";
            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@Id", id),
                new NpgsqlParameter("@ChurchId", churchId)
            };

            await _postgresRepository.ExecuteNonQueryAsync(deleteQuery, parameters);
            TempData["SuccessMessage"] = "Registro infantil eliminado correctamente.";

            return RedirectToAction(nameof(Index));
        }

        // 📊 Reporte Histórico de Asistencia
        [HttpGet]
        public async Task<IActionResult> Report(DateTime? startDate, DateTime? endDate)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            if (!await UserCanManageSundaySchoolAsync())
            {
                TempData["ErrorMessage"] = "No tienes permisos para ver reportes de asistencia.";
                return RedirectToAction("Index", "Dashboard");
            }

            // Fechas por defecto: Desde el primer día del mes actual hasta hoy
            DateTime start = startDate ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            DateTime end = endDate ?? DateTime.Now.Date;

            string query = @"
        SELECT 
            a.id AS attendance_id,
            c.first_name,
            c.last_name,
            c.parent_guardian_name,
            c.parent_phone,
            a.check_in_time,
            a.check_out_time,
            a.checked_in_by,
            a.checked_out_by,
            a.security_code_used,
            a.status
        FROM sunday_school_attendance a
        INNER JOIN children c ON a.child_id = c.id
        WHERE a.church_id = @ChurchId
          AND DATE(a.check_in_time) BETWEEN @StartDate AND @EndDate
        ORDER BY a.check_in_time DESC;";

            var parameters = new NpgsqlParameter[]
            {
        new NpgsqlParameter("@ChurchId", churchId),
        new NpgsqlParameter("@StartDate", start.Date),
        new NpgsqlParameter("@EndDate", end.Date)
            };

            var dt = await _postgresRepository.ExecuteQueryAsync(query, parameters);

            ViewBag.StartDate = start.ToString("yyyy-MM-dd");
            ViewBag.EndDate = end.ToString("yyyy-MM-dd");
            ViewBag.CurrentChurchId = churchId;

            return View(dt);
        }
    }
}