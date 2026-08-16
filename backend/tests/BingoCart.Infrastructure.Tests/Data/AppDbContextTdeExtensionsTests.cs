using System;
using System.Threading.Tasks;
using BingoCart.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace BingoCart.Infrastructure.Tests.Data;

/// <summary>
/// Test de integración liviano (mismo patrón que <see cref="AppDbContextTests"/>): requiere el
/// servicio `db` de docker-compose.yml corriendo y accesible en localhost:14330. Verifica que
/// <c>AppDbContextTdeExtensions.EnsureTdeEnabledAsync</c> (corrective loop VERIFY→CODE de
/// FEAT-001a: el threat model declaraba TDE "Mitigado" pero nunca se ejecutaba, Bloque 1) deja la
/// base con TDE realmente activo — <c>is_encrypted = 1</c> en <c>sys.databases</c>, verificado con
/// una consulta independiente del método bajo test, no solo confiando en que no haya lanzado.
/// Usa su propia base descartable (Rule #0 de testing.instructions.md), nunca `BingoCart`.
/// </summary>
public class AppDbContextTdeExtensionsTests : IAsyncLifetime
{
    // Mismo password de desarrollo local declarado en docker-compose.yml (Block 1) — nunca es un
    // secreto real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_Tde;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string MasterConnectionString =
        "Server=localhost,14330;Database=master;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    // Password de la master key de TDE, deliberadamente distinto del de sa (mismo criterio pedido
    // para el password real de la Api, ver appsettings.Development.json).
    private const string MasterKeyPassword = "BingoCart_TdeTest_2026#";

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);

        // TDE requiere que la base ya exista: la migración corre primero, igual que en el
        // arranque real de la Api (Program.cs).
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task EnsureTdeEnabledAsync_SobreUnaBaseRecienMigrada_DejaLaBaseRealmenteEncriptada()
    {
        await _context.EnsureTdeEnabledAsync(MasterKeyPassword);

        var estaEncriptada = await ConsultarIsEncrypted();

        Assert.True(estaEncriptada);
    }

    [Fact]
    public async Task EnsureTdeEnabledAsync_LlamadoDosVeces_EsIdempotenteYNoLanza()
    {
        await _context.EnsureTdeEnabledAsync(MasterKeyPassword);

        var exception = await Record.ExceptionAsync(
            () => _context.EnsureTdeEnabledAsync(MasterKeyPassword));

        Assert.Null(exception);
    }

    private static async Task<bool> ConsultarIsEncrypted()
    {
        await using var connection = new SqlConnection(MasterConnectionString);
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT is_encrypted FROM sys.databases WHERE name = 'BingoCartTests_Tde';";

        var result = await command.ExecuteScalarAsync();
        return result is bool value && value;
    }
}
