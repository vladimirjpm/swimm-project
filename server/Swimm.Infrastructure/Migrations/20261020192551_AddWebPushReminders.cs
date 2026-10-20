using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Swimm.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebPushReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Sys_HubGroupReminderMutes",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_HubGroupReminderMutes", x => new { x.UserId, x.HubGroupId });
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupReminderMutes_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_HubGroupReminderMutes_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_ReminderDeliveries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    HubGroupId = table.Column<int>(type: "integer", nullable: false),
                    SessionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SessionStart = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Channel = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Result = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Devices = table.Column<int>(type: "integer", nullable: false),
                    Delivered = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_ReminderDeliveries", x => x.Id);
                    table.CheckConstraint("CK_Sys_ReminderDeliveries_Channel", "\"Channel\" IN ('push', 'email')");
                    table.CheckConstraint("CK_Sys_ReminderDeliveries_Kind", "\"Kind\" IN ('reminder', 'cancelled')");
                    table.CheckConstraint("CK_Sys_ReminderDeliveries_Result", "\"Result\" IN ('pending', 'sent', 'gone', 'failed')");
                    table.ForeignKey(
                        name: "FK_Sys_ReminderDeliveries_HubGroups_HubGroupId",
                        column: x => x.HubGroupId,
                        principalTable: "HubGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Sys_ReminderDeliveries_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserNotificationSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RemindersEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    LeadKind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EmailFallback = table.Column<bool>(type: "boolean", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserNotificationSettings", x => x.UserId);
                    table.CheckConstraint("CK_Sys_UserNotificationSettings_LeadKind", "\"LeadKind\" IN ('1h', '2h', 'evening')");
                    table.ForeignKey(
                        name: "FK_Sys_UserNotificationSettings_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Sys_UserPushSubscriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Endpoint = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    P256dh = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Auth = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DeviceLabel = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFailureAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailureCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sys_UserPushSubscriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sys_UserPushSubscriptions_Sys_AppUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "Sys_AppUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Sys_HubGroupReminderMutes_HubGroupId",
                table: "Sys_HubGroupReminderMutes",
                column: "HubGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ReminderDeliveries_CreatedAt",
                table: "Sys_ReminderDeliveries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Sys_ReminderDeliveries_HubGroupId",
                table: "Sys_ReminderDeliveries",
                column: "HubGroupId");

            migrationBuilder.CreateIndex(
                name: "UX_Sys_ReminderDeliveries_Session",
                table: "Sys_ReminderDeliveries",
                columns: new[] { "UserId", "HubGroupId", "SessionDate", "SessionStart", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserPushSubscriptions_Endpoint",
                table: "Sys_UserPushSubscriptions",
                column: "Endpoint",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sys_UserPushSubscriptions_UserId",
                table: "Sys_UserPushSubscriptions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Sys_HubGroupReminderMutes");

            migrationBuilder.DropTable(
                name: "Sys_ReminderDeliveries");

            migrationBuilder.DropTable(
                name: "Sys_UserNotificationSettings");

            migrationBuilder.DropTable(
                name: "Sys_UserPushSubscriptions");
        }
    }
}
