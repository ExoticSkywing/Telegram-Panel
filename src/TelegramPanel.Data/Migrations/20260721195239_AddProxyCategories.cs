using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramPanel.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProxyCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "OutboundProxies",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ProxyCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Color = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxyCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboundProxies_CategoryId",
                table: "OutboundProxies",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_ProxyCategories_Name",
                table: "ProxyCategories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_OutboundProxies_ProxyCategories_CategoryId",
                table: "OutboundProxies",
                column: "CategoryId",
                principalTable: "ProxyCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OutboundProxies_ProxyCategories_CategoryId",
                table: "OutboundProxies");

            migrationBuilder.DropTable(
                name: "ProxyCategories");

            migrationBuilder.DropIndex(
                name: "IX_OutboundProxies_CategoryId",
                table: "OutboundProxies");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "OutboundProxies");
        }
    }
}
