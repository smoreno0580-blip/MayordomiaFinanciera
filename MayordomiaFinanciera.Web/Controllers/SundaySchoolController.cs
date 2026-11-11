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

        // 📋 1. Listado y Panel Principal de Check-In
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return RedirectToAction("Index", "Home");

            string query = @"
                SELECT c.id, c.first_name, c.last_name, c.allergy_notes, c.parent_guardian_name, c.parent_phone, c.secure_code,
                       a.id as attendance_id, a.check_in_time, a.status
                FROM children c
                LEFT JOIN sunday_school_attendance a ON c.id = a.child_id AND a.status = 'CHECKED_IN' AND DATE(a.check_in_time) = CURRENT_DATE
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

        // 🚪 3. Realizar Check-Out (Retiro seguro con código)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CheckOut(Guid attendanceId, string returingGuardian)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty || attendanceId == Guid.Empty) return RedirectToAction(nameof(Index));

            string updateQuery = @"
                UPDATE sunday_school_attendance
                SET check_out_time = CURRENT_TIMESTAMP, checked_out_by = @ReturingGuardian, status = 'CHECKED_OUT'
                WHERE id = @AttendanceId AND church_id = @ChurchId;";

            var parameters = new NpgsqlParameter[]
            {
                new NpgsqlParameter("@AttendanceId", attendanceId),
                new NpgsqlParameter("@ChurchId", churchId),
                new NpgsqlParameter("@ReturingGuardian", (object?)returingGuardian ?? "Padre / Tutor")
            };

            await _postgresRepository.ExecuteNonQueryAsync(updateQuery, parameters);
            TempData["SuccessMessage"] = "¡Check-Out completado! Niño entregado de forma segura.";

            return RedirectToAction(nameof(Index));
        }

        // ➕ 4. Registrar Nuevo Niño en el Sistema
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveChild(string firstName, string lastName, DateTime? birthDate, string allergyNotes, string parentName, string parentPhone, string secureCode)
        {
            Guid churchId = GetCurrentChurchId();
            if (churchId == Guid.Empty) return Unauthorized();

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
                new NpgsqlParameter("@SecureCode", (object?)secureCode ?? "1234")
            };

            await _postgresRepository.ExecuteNonQueryAsync(insertQuery, parameters);
            TempData["SuccessMessage"] = "¡Niño registrado correctamente en la base de datos de la Escuela Dominical!";

            return RedirectToAction(nameof(Index));
        }
    }
}