using System;
using System.Security.Claims;
using System.Threading.Tasks;
using MayordomiaFinanciera.Web.Models.Church;
using MayordomiaFinanciera.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MayordomiaFinanciera.Web.Controllers
{
    [Authorize]
    public class VolunteersController : Controller
    {
        private readonly IChurchRepository _churchRepository;

        public VolunteersController(IChurchRepository churchRepository)
        {
            _churchRepository = churchRepository;
        }

        private Guid GetCurrentUserId()
        {
            var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (Guid.TryParse(claim, out var userId)) return userId;
            throw new UnauthorizedAccessException("Usuario no autenticado.");
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            Guid userId = GetCurrentUserId();
            var church = await _churchRepository.GetChurchByUserIdAsync(userId);
            if (church == null) return RedirectToAction("Index", "Home");

            var teams = await _churchRepository.GetVolunteerTeamsAsync(church.Id);
            ViewBag.Church = church;
            return View(teams);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateTeam(VolunteerTeamDto model)
        {
            Guid userId = GetCurrentUserId();
            var church = await _churchRepository.GetChurchByUserIdAsync(userId);
            if (church == null) return Unauthorized();

            model.ChurchId = church.Id;
            await _churchRepository.CreateVolunteerTeamAsync(model);
            TempData["SuccessMessage"] = "¡Equipo de servicio creado exitosamente!";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> Details(Guid id)
        {
            var team = await _churchRepository.GetVolunteerTeamByIdAsync(id);
            if (team == null) return NotFound();

            Guid userId = GetCurrentUserId();
            ViewBag.Church = await _churchRepository.GetChurchByUserIdAsync(userId);
            return View(team);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddMember(VolunteerMemberDto model)
        {
            await _churchRepository.AddVolunteerMemberAsync(model);
            TempData["SuccessMessage"] = "¡Voluntario agregado al equipo correctamente!";
            return RedirectToAction("Details", new { id = model.TeamId });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveMember(Guid memberId, Guid teamId)
        {
            await _churchRepository.RemoveVolunteerMemberAsync(memberId);
            TempData["SuccessMessage"] = "Voluntario removido del equipo.";
            return RedirectToAction("Details", new { id = teamId });
        }
    }
}