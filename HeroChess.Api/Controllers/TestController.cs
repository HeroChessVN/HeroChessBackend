using HeroChess.Api.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HeroChess.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TestController : ControllerBase
{
    private readonly AppDbContext _context;

    public TestController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet("database")]
    public async Task<IActionResult> TestDatabase()
    {
        bool canConnect = await _context.Database.CanConnectAsync();

        return Ok(new
        {
            databaseConnected = canConnect,
            message = canConnect
                ? "Connected to Supabase PostgreSQL"
                : "Cannot connect to database"
        });
    }
}