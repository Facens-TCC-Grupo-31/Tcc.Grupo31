using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameGraphNodeCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "X",
                table: "GraphNodes",
                newName: "Longitude");

            migrationBuilder.RenameColumn(
                name: "Y",
                table: "GraphNodes",
                newName: "Latitude");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Longitude",
                table: "GraphNodes",
                newName: "X");

            migrationBuilder.RenameColumn(
                name: "Latitude",
                table: "GraphNodes",
                newName: "Y");
        }
    }
}
