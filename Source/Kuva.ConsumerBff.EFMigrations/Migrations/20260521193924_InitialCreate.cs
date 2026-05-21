using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuva.ConsumerBff.EFMigrations.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "consumer_bff");

            migrationBuilder.CreateTable(
                name: "api_request_audits",
                schema: "consumer_bff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CorrelationId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    Path = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: false),
                    ElapsedMs = table.Column<int>(type: "int", nullable: false),
                    SanitizedError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ClientAppVersion = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    DevicePlatform = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    IpHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    UserAgentHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_api_request_audits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "consumer_order_drafts",
                schema: "consumer_bff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StoreId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_consumer_order_drafts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "idempotency_keys",
                schema: "consumer_bff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    ConsumerId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequestHash = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                    ResourceType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResponsePayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_idempotency_keys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "external_service_calls",
                schema: "consumer_bff",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApiRequestAuditId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ServiceName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Operation = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Method = table.Column<string>(type: "nvarchar(12)", maxLength: 12, nullable: false),
                    UrlTemplate = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    StatusCode = table.Column<int>(type: "int", nullable: true),
                    ElapsedMs = table.Column<int>(type: "int", nullable: false),
                    CorrelationId = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    SanitizedError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApiRequestAuditEntityId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_external_service_calls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_external_service_calls_api_request_audits_ApiRequestAuditEntityId",
                        column: x => x.ApiRequestAuditEntityId,
                        principalSchema: "consumer_bff",
                        principalTable: "api_request_audits",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_api_request_audits_consumer_id_created_at",
                schema: "consumer_bff",
                table: "api_request_audits",
                columns: new[] { "ConsumerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_api_request_audits_correlation_id",
                schema: "consumer_bff",
                table: "api_request_audits",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_api_request_audits_status_code_created_at",
                schema: "consumer_bff",
                table: "api_request_audits",
                columns: new[] { "StatusCode", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_consumer_order_drafts_consumer_id_expires_at",
                schema: "consumer_bff",
                table: "consumer_order_drafts",
                columns: new[] { "ConsumerId", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_external_service_calls_ApiRequestAuditEntityId",
                schema: "consumer_bff",
                table: "external_service_calls",
                column: "ApiRequestAuditEntityId");

            migrationBuilder.CreateIndex(
                name: "IX_idempotency_keys_expires_at",
                schema: "consumer_bff",
                table: "idempotency_keys",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "UX_idempotency_keys_key_consumer_id",
                schema: "consumer_bff",
                table: "idempotency_keys",
                columns: new[] { "Key", "ConsumerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "consumer_order_drafts",
                schema: "consumer_bff");

            migrationBuilder.DropTable(
                name: "external_service_calls",
                schema: "consumer_bff");

            migrationBuilder.DropTable(
                name: "idempotency_keys",
                schema: "consumer_bff");

            migrationBuilder.DropTable(
                name: "api_request_audits",
                schema: "consumer_bff");
        }
    }
}
