using MayordomiaFinanciera.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using System;
using System.Data;
using System.Threading.Tasks;

public class DonationsController : Controller
{
    private readonly PostgresRepository _postgresRepository; // O tu servicio de base de datos actual

    public DonationsController(PostgresRepository postgresRepository)
    {
        _postgresRepository = postgresRepository;
    }

    // Vista pública de Donaciones (para la Landing Page o menú general)
    [HttpGet]
    public async Task<IActionResult> Index()
    {
        string query = "SELECT * FROM donation_settings WHERE is_active = true ORDER BY id;";
        var dt = await _postgresRepository.ExecuteQueryAsync(query, null);
        return View(dt);
    }

    // Vista de Administración / Edición (Solo Admin)
    [Authorize(Roles = "admin")]
    [HttpGet]
    public async Task<IActionResult> Manage()
    {
        string query = "SELECT * FROM donation_settings ORDER BY id;";
        var dt = await _postgresRepository.ExecuteQueryAsync(query, null);
        return View(dt);
    }

    // Acción para actualizar o insertar la información de pago
    [Authorize(Roles = "admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateMethod(int id, string title, string details, bool isActive)
    {
        string query = @"UPDATE donation_settings 
                         SET title = @title, details = @details, is_active = @isActive, updated_at = CURRENT_TIMESTAMP 
                         WHERE id = @id;";

        var parameters = new NpgsqlParameter[]
        {
            new NpgsqlParameter("@id", id),
            new NpgsqlParameter("@title", title),
            new NpgsqlParameter("@details", details),
            new NpgsqlParameter("@isActive", isActive)
        };

        await _postgresRepository.ExecuteNonQueryAsync(query, parameters);
        return RedirectToAction(nameof(Manage));
    }
}