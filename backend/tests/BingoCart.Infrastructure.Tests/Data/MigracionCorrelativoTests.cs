using System;
using System.Linq;
using System.Threading.Tasks;
using BingoCart.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BingoCart.Infrastructure.Tests.Data;

/// <summary>
/// Tests de integración de la migración <c>AddNumeroCorrelativoACartones</c> (spec FEAT-009d,
/// Block 1) contra SQL Server real — requiere el servicio `db` de docker-compose.yml en
/// localhost:14330. Base propia y descartable, eliminada en <see cref="DisposeAsync"/> (Rule #0 de
/// testing.instructions.md).
///
/// La migración se ejercita en las dos direcciones a partir de un esquema PREVIO poblado con
/// cartones sembrados por SQL crudo: el backfill solo se puede verificar sobre filas que ya
/// existían cuando la columna no existía, y esas filas no se pueden insertar con EF Core (el
/// modelo actual ya incluye <c>NumeroCorrelativo</c>).
/// </summary>
public sealed class MigracionCorrelativoTests : IAsyncLifetime
{
    // Mismo password de desarrollo local declarado en docker-compose.yml — nunca es un secreto
    // real, solo la credencial del contenedor SQL Server efímero de desarrollo.
    private const string ConnectionString =
        "Server=localhost,14330;Database=BingoCartTests_MigracionCorrelativo;User Id=sa;" +
        "Password=BingoCart_Dev2026!;TrustServerCertificate=True;Encrypt=True;";

    private const string MigracionAnterior = "AddTipoEnvioYCompraIdAEnviosMail";
    private const string MigracionCorrelativo = "AddNumeroCorrelativoACartones";

    private readonly Guid _bingoConTresCartones = Guid.NewGuid();
    private readonly Guid _bingoConDosCartones = Guid.NewGuid();

    private AppDbContext _context = null!;

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        _context = new AppDbContext(options);
        await _context.Database.EnsureDeletedAsync();

        await MigrarAsync(MigracionAnterior);
        await SembrarCartonesPreexistentesAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.Database.EnsureDeletedAsync();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task Down_RevierteColumnaEIndiceSinDejarEstadoParcial()
    {
        await MigrarAsync(MigracionCorrelativo);

        Assert.Equal("NO", await NulabilidadDeColumnaAsync("Cartones", "NumeroCorrelativo"));
        Assert.True(await ExisteIndiceAsync("Cartones", "IX_Cartones_BingoId_NumeroCorrelativo"));
        Assert.Equal(new[] { 1, 2, 3 }, await CorrelativosDeAsync(_bingoConTresCartones));
        Assert.Equal(new[] { 1, 2 }, await CorrelativosDeAsync(_bingoConDosCartones));

        await MigrarAsync(MigracionAnterior);

        Assert.Equal(
            new[] { "BingoId", "Id", "NumerosSerializados" },
            await ColumnasDeAsync("Cartones"));
        Assert.False(await ExisteIndiceAsync("Cartones", "IX_Cartones_BingoId_NumeroCorrelativo"));
        Assert.True(await ExisteIndiceAsync("Cartones", "IX_Cartones_BingoId_NumerosSerializados"));
    }

    private Task MigrarAsync(string migracionObjetivo) =>
        _context.GetService<IMigrator>().MigrateAsync(migracionObjetivo);

    private async Task SembrarCartonesPreexistentesAsync()
    {
        await InsertarBingoAsync(_bingoConTresCartones, "Bingo de tres cartones");
        await InsertarBingoAsync(_bingoConDosCartones, "Bingo de dos cartones");

        await InsertarCartonAsync(_bingoConTresCartones, "1,2,3,4,5,6,7,8,9,10");
        await InsertarCartonAsync(_bingoConTresCartones, "11,12,13,14,15,16,17,18,19,20");
        await InsertarCartonAsync(_bingoConTresCartones, "21,22,23,24,25,26,27,28,29,30");
        await InsertarCartonAsync(_bingoConDosCartones, "31,32,33,34,35,36,37,38,39,40");
        await InsertarCartonAsync(_bingoConDosCartones, "41,42,43,44,45,46,47,48,49,50");
    }

    private Task InsertarBingoAsync(Guid bingoId, string nombreEvento) =>
        _context.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO Bingos (Id, OrganizadorId, NombreEvento, FechaSorteoUtc, FechaCreacionUtc,
                                CantidadCartones, CostoPorCarton)
            VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6});
            """,
            bingoId,
            Guid.NewGuid(),
            nombreEvento,
            DateTime.UtcNow.AddDays(5),
            DateTime.UtcNow,
            10,
            100m);

    private Task InsertarCartonAsync(Guid bingoId, string numerosSerializados) =>
        _context.Database.ExecuteSqlRawAsync(
            "INSERT INTO Cartones (Id, BingoId, NumerosSerializados) VALUES ({0}, {1}, {2});",
            Guid.NewGuid(),
            bingoId,
            numerosSerializados);

    // Los correlativos se leen ordenados por Id, que es el mismo criterio con el que el backfill
    // los asigna (ver el comentario de la migración): el resultado esperado es 1..N en ese orden.
    private async Task<int[]> CorrelativosDeAsync(Guid bingoId) =>
        await _context.Database
            .SqlQueryRaw<int>(
                "SELECT NumeroCorrelativo AS Value FROM Cartones WHERE BingoId = {0} ORDER BY Id;",
                bingoId)
            .ToArrayAsync();

    private async Task<string[]> ColumnasDeAsync(string tabla) =>
        await _context.Database
            .SqlQueryRaw<string>(
                """
                SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = {0} ORDER BY COLUMN_NAME;
                """,
                tabla)
            .ToArrayAsync();

    private async Task<string?> NulabilidadDeColumnaAsync(string tabla, string columna) =>
        (await _context.Database
            .SqlQueryRaw<string>(
                """
                SELECT IS_NULLABLE AS Value FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = {0} AND COLUMN_NAME = {1};
                """,
                tabla,
                columna)
            .ToArrayAsync())
        .SingleOrDefault();

    private async Task<bool> ExisteIndiceAsync(string tabla, string indice) =>
        (await _context.Database
            .SqlQueryRaw<int>(
                """
                SELECT COUNT(*) AS Value FROM sys.indexes
                WHERE name = {1} AND object_id = OBJECT_ID({0});
                """,
                tabla,
                indice)
            .ToArrayAsync())
        .Single() > 0;
}
