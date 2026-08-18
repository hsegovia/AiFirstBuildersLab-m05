using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoCart.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBingosYCartones : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bingos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizadorId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NombreEvento = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FechaSorteoUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FechaCreacionUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CantidadCartones = table.Column<int>(type: "int", nullable: false),
                    CostoPorCarton = table.Column<decimal>(type: "decimal(10,2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bingos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Cartones",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BingoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    NumerosSerializados = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Cartones", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Cartones_Bingos_BingoId",
                        column: x => x.BingoId,
                        principalTable: "Bingos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bingos_OrganizadorId",
                table: "Bingos",
                column: "OrganizadorId");

            migrationBuilder.CreateIndex(
                name: "IX_Cartones_BingoId_NumerosSerializados",
                table: "Cartones",
                columns: new[] { "BingoId", "NumerosSerializados" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Cartones");

            migrationBuilder.DropTable(
                name: "Bingos");
        }
    }
}
