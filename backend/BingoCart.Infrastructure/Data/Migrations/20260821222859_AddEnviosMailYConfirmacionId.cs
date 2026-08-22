using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoCart.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEnviosMailYConfirmacionId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `Compras` YA tiene filas reales en `main` (FEAT-009a) — a diferencia del scaffold por
            // defecto de EF Core (que hubiera puesto el MISMO Guid vacío en todas las filas
            // existentes vía `defaultValue`), la columna se agrega NULLABLE primero, se backfillea
            // fila por fila con un Guid PROPIO vía `NEWID()` (mitigación R-06 del threat model: cada
            // compra pre-existente se trata como su propio lote de confirmación de un solo
            // elemento, nunca comparte ConfirmacionId con otra), y recién después se vuelve NOT
            // NULL. `NEWID()` de SQL Server se evalúa una vez POR FILA dentro de un UPDATE (a
            // diferencia de una constante), así que cada Compra recibe un valor distinto.
            migrationBuilder.AddColumn<Guid>(
                name: "ConfirmacionId",
                table: "Compras",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE Compras SET ConfirmacionId = NEWID() WHERE ConfirmacionId IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "ConfirmacionId",
                table: "Compras",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "EnviosMail",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConfirmacionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CompradorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Estado = table.Column<int>(type: "int", nullable: false),
                    Intentos = table.Column<int>(type: "int", nullable: false),
                    ProximoIntentoUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnviosMail", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EnviosMail_Estado_ProximoIntentoUtc",
                table: "EnviosMail",
                columns: new[] { "Estado", "ProximoIntentoUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EnviosMail");

            migrationBuilder.DropColumn(
                name: "ConfirmacionId",
                table: "Compras");
        }
    }
}
