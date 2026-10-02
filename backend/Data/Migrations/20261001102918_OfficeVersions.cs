using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class OfficeVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Files",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateTable(
                name: "FileVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: false),
                    SaveId = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedById = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FileVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FileVersions_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FileVersions_Users_CreatedById",
                        column: x => x.CreatedById,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfficeSessions",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    InitialVersion = table.Column<int>(type: "integer", nullable: false),
                    LastSavedVersion = table.Column<int>(type: "integer", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeSessions", x => x.Key);
                    table.ForeignKey(
                        name: "FK_OfficeSessions_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OfficeParticipants",
                columns: table => new
                {
                    SessionKey = table.Column<string>(type: "character varying(128)", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanEdit = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeParticipants", x => new { x.SessionKey, x.UserId });
                    table.ForeignKey(
                        name: "FK_OfficeParticipants_OfficeSessions_SessionKey",
                        column: x => x.SessionKey,
                        principalTable: "OfficeSessions",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OfficeParticipants_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_CreatedById",
                table: "FileVersions",
                column: "CreatedById");

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileId_Number",
                table: "FileVersions",
                columns: new[] { "FileId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FileVersions_FileId_SaveId",
                table: "FileVersions",
                columns: new[] { "FileId", "SaveId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfficeParticipants_UserId",
                table: "OfficeParticipants",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_OfficeSessions_FileId_ClosedAt",
                table: "OfficeSessions",
                columns: new[] { "FileId", "ClosedAt" });

            // Existing MinIO objects become immutable version-one snapshots without copying bytes.
            migrationBuilder.Sql("""
                INSERT INTO "FileVersions" ("Id", "FileId", "Number", "ObjectKey", "Size", "ContentType", "CreatedAt", "CreatedById")
                SELECT gen_random_uuid(), "Id", 1, "ObjectKey", "Size", "ContentType", "CreatedAt", "OwnerId" FROM "Files";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FileVersions");

            migrationBuilder.DropTable(
                name: "OfficeParticipants");

            migrationBuilder.DropTable(
                name: "OfficeSessions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Files");
        }
    }
}
