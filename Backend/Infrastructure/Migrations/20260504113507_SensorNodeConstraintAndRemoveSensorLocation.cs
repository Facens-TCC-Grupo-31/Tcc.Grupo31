using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SensorNodeConstraintAndRemoveSensorLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Sensors");

            migrationBuilder.CreateIndex(
                name: "IX_Sensors_NodeId",
                table: "Sensors",
                column: "NodeId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sensors_IsActive_RequiresNode",
                table: "Sensors",
                sql: "\"IsActive\" = false OR \"NodeId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Sensors_GraphNodes_NodeId",
                table: "Sensors",
                column: "NodeId",
                principalTable: "GraphNodes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sensors_GraphNodes_NodeId",
                table: "Sensors");

            migrationBuilder.DropIndex(
                name: "IX_Sensors_NodeId",
                table: "Sensors");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sensors_IsActive_RequiresNode",
                table: "Sensors");

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Sensors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Sensors",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
