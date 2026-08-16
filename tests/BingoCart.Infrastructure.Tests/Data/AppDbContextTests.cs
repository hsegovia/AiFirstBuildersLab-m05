using System;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Data;

/// <summary>
/// Test de integración liviano: requiere el servicio `db` de docker-compose.yml corriendo
/// (`docker compose up -d db`) y accesible en localhost:14330 (Block 1 del spec FEAT-001a).
/// No usa datos existentes: aplica la migración InicialCreate sobre una base propia y la
/// elimina al finalizar (Rule #0 de testing.instructions.md).
/// </summary>
public class AppDbContextTests : IDisposable
{
    // Mismo password de desarrollo local declarado en docker-compose.yml (Block 1) — nunca es un
    // secreto real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_AppDbContext;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private readonly AppDbContext _context;

    public AppDbContextTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
    }

    [Fact]
    public void PuedeConstruirseYAplicarMigracion()
    {
        _context.Database.Migrate();

        var pendingMigrations = _context.Database.GetPendingMigrations();

        Assert.Empty(pendingMigrations);
    }

    public void Dispose()
    {
        _context.Database.EnsureDeleted();
        _context.Dispose();
        GC.SuppressFinalize(this);
    }
}
