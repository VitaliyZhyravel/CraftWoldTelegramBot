using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApplication1.Migrations
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TelegramUsers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramUserId = table.Column<long>(type: "INTEGER", nullable: false),
                    ChatId = table.Column<long>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackedPools",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PoolAddress = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Token0Address = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Token1Address = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Token0Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Token1Symbol = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    LastKnownPrice = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    LastKnownInversePrice = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: true),
                    LastKnownTick = table.Column<int>(type: "INTEGER", nullable: true),
                    LastPolledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedPools", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PendingPoolAddress = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramChatSessions_TelegramUsers_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "TelegramUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PriceAlertSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TelegramUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackedPoolId = table.Column<int>(type: "INTEGER", nullable: false),
                    ThresholdPercent = table.Column<decimal>(type: "TEXT", precision: 18, scale: 8, nullable: false),
                    BasePrice = table.Column<decimal>(type: "TEXT", precision: 38, scale: 18, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAlertedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceAlertSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PriceAlertSubscriptions_TelegramUsers_TelegramUserId",
                        column: x => x.TelegramUserId,
                        principalTable: "TelegramUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PriceAlertSubscriptions_TrackedPools_TrackedPoolId",
                        column: x => x.TrackedPoolId,
                        principalTable: "TrackedPools",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PriceAlertSubscriptions_TelegramUserId_TrackedPoolId",
                table: "PriceAlertSubscriptions",
                columns: new[] { "TelegramUserId", "TrackedPoolId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PriceAlertSubscriptions_TrackedPoolId",
                table: "PriceAlertSubscriptions",
                column: "TrackedPoolId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramChatSessions_TelegramUserId",
                table: "TelegramChatSessions",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramUsers_TelegramUserId",
                table: "TelegramUsers",
                column: "TelegramUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedPools_PoolAddress",
                table: "TrackedPools",
                column: "PoolAddress",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PriceAlertSubscriptions");

            migrationBuilder.DropTable(
                name: "TelegramChatSessions");

            migrationBuilder.DropTable(
                name: "TrackedPools");

            migrationBuilder.DropTable(
                name: "TelegramUsers");
        }
    }
}
