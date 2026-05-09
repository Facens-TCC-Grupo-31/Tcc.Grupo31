using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepotNodeMapping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepotNodeMappings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    DepotKey = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    GraphSignature = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Latitude = table.Column<double>(type: "double precision", nullable: false),
                    Longitude = table.Column<double>(type: "double precision", nullable: false),
                    NodeId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepotNodeMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepotNodeMappings_GraphNodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "GraphNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepotNodeMappings_DepotKey_GraphSignature",
                table: "DepotNodeMappings",
                columns: new[] { "DepotKey", "GraphSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepotNodeMappings_NodeId",
                table: "DepotNodeMappings",
                column: "NodeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepotNodeMappings");
        }
    }
}
