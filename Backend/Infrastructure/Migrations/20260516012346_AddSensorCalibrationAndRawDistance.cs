using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorCalibrationAndRawDistance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CalibratedAtUtc",
                table: "Sensors",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalibrationMethod",
                table: "Sensors",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<short>(
                name: "CalibrationSampleCount",
                table: "Sensors",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmptyDistanceMm",
                table: "Sensors",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SensorDistanceMeasurements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    SensorId = table.Column<long>(type: "bigint", nullable: false),
                    DistanceMm = table.Column<int>(type: "integer", nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensorDistanceMeasurements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SensorDistanceMeasurements_Sensors_SensorId",
                        column: x => x.SensorId,
                        principalTable: "Sensors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SensorDistanceMeasurements_SensorId_ReceivedAtUtc",
                table: "SensorDistanceMeasurements",
                columns: new[] { "SensorId", "ReceivedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SensorDistanceMeasurements");

            migrationBuilder.DropColumn(
                name: "CalibratedAtUtc",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "CalibrationMethod",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "CalibrationSampleCount",
                table: "Sensors");

            migrationBuilder.DropColumn(
                name: "EmptyDistanceMm",
                table: "Sensors");
        }
    }
}
