using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoCart.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNumeroCorrelativoACartones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `Cartones` YA tiene filas reales, y el backfill necesita un valor DISTINTO por fila
            // dentro de cada bingo — que es exactamente lo que `AddColumn(..., defaultValue: n)` no
            // puede hacer (pondría el mismo valor en todas y el índice único de abajo fallaría al
            // segundo cartón de cada bingo). Se replica entonces el patrón de tres pasos ya
            // razonado en 20260821222859_AddEnviosMailYConfirmacionId: columna nullable → backfill
            // por SQL → NOT NULL, y recién después el índice.
            migrationBuilder.AddColumn<int>(
                name: "NumeroCorrelativo",
                table: "Cartones",
                type: "int",
                nullable: true);

            // ORDER BY Id, y no otra cosa, porque no hay otra cosa: `Cartones` tiene exactamente
            // tres columnas (Id, BingoId, NumerosSerializados) y ninguna refleja el orden de
            // inserción — no hay FechaCreacionUtc ni IDENTITY—, y ordenar por NumerosSerializados
            // dependería del collation, que este proyecto no fija en ningún lado. `Id` es la única
            // opción determinista y estable entre entornos.
            //
            // Consecuencia asumida (D-08 / R-07 del PRD FEAT-009d): los cartones PREEXISTENTES
            // reciben su correlativo en orden de GUID, arbitrario respecto de cuándo se generaron.
            // Los creados a partir de este ticket lo reciben en orden de generación (BingoService).
            // Ambos rangos son 1..N por bingo y estables en el tiempo: el conjunto de cartones de
            // un bingo no cambia después de crearse, así que nunca hace falta renumerar.
            migrationBuilder.Sql(
                """
                WITH CartonesNumerados AS (
                    SELECT NumeroCorrelativo,
                           ROW_NUMBER() OVER (PARTITION BY BingoId ORDER BY Id) AS Correlativo
                    FROM Cartones
                )
                UPDATE CartonesNumerados SET NumeroCorrelativo = Correlativo;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "NumeroCorrelativo",
                table: "Cartones",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Cartones_BingoId_NumeroCorrelativo",
                table: "Cartones",
                columns: new[] { "BingoId", "NumeroCorrelativo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Cartones_BingoId_NumeroCorrelativo",
                table: "Cartones");

            migrationBuilder.DropColumn(
                name: "NumeroCorrelativo",
                table: "Cartones");
        }
    }
}
