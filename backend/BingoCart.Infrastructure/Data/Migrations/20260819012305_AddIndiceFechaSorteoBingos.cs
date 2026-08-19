using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoCart.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIndiceFechaSorteoBingos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Bingos_FechaSorteoUtc",
                table: "Bingos",
                column: "FechaSorteoUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Bingos_FechaSorteoUtc",
                table: "Bingos");
        }
    }
}
