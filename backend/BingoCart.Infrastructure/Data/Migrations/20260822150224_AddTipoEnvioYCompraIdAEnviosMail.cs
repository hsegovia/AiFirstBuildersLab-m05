using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoCart.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTipoEnvioYCompraIdAEnviosMail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // `ConfirmacionId` pasa a NULLABLE (spec FEAT-009c, Block 3): desde este ticket un
            // EnvioMail de tipo Cancelacion no lo setea (usa CompraId en su lugar) —
            // TipoEnvioMail.Cancelacion discrimina cuál de los dos aplica.
            migrationBuilder.AlterColumn<Guid>(
                name: "ConfirmacionId",
                table: "EnviosMail",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            // `CompraId` nuevo, nullable — solo seteado para TipoEnvio = Cancelacion.
            migrationBuilder.AddColumn<Guid>(
                name: "CompraId",
                table: "EnviosMail",
                type: "uniqueidentifier",
                nullable: true);

            // `TipoEnvio` NOT NULL con DEFAULT 0 (TipoEnvioMail.Confirmacion): backfill correcto sin
            // pérdida de datos — toda fila de `EnviosMail` que ya existe en `main` hoy fue creada por
            // FEAT-009b (outbox de confirmación de compra), antes de que este ticket introdujera la
            // cancelación, así que es imposible que una fila preexistente sea de tipo Cancelacion.
            migrationBuilder.AddColumn<int>(
                name: "TipoEnvio",
                table: "EnviosMail",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompraId",
                table: "EnviosMail");

            migrationBuilder.DropColumn(
                name: "TipoEnvio",
                table: "EnviosMail");

            migrationBuilder.AlterColumn<Guid>(
                name: "ConfirmacionId",
                table: "EnviosMail",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
