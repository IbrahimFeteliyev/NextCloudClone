using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class TrashAndManualVersions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Folders_ParentFolderId_Name",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Files_ParentFolderId_Name",
                table: "Files");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Folders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrashRootId",
                table: "Folders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Files",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TrashRootId",
                table: "Files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OfficeSaves",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfficeSaves", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfficeSaves_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentFolderId_Name",
                table: "Folders",
                columns: new[] { "ParentFolderId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Files_ParentFolderId_Name",
                table: "Files",
                columns: new[] { "ParentFolderId", "Name" },
                unique: true,
                filter: "\"DeletedAt\" IS NULL");

            migrationBuilder.Sql("INSERT INTO \"OfficeSaves\" (\"Id\", \"FileId\") SELECT \"SaveId\", \"FileId\" FROM \"FileVersions\" WHERE \"SaveId\" IS NOT NULL AND \"SaveId\" NOT LIKE 'text:%' AND \"SaveId\" NOT LIKE 'restore:%' ON CONFLICT DO NOTHING;");
            migrationBuilder.CreateIndex(
                name: "IX_OfficeSaves_FileId",
                table: "OfficeSaves",
                column: "FileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OfficeSaves");

            migrationBuilder.DropIndex(
                name: "IX_Folders_ParentFolderId_Name",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Files_ParentFolderId_Name",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "TrashRootId",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "TrashRootId",
                table: "Files");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_ParentFolderId_Name",
                table: "Folders",
                columns: new[] { "ParentFolderId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Files_ParentFolderId_Name",
                table: "Files",
                columns: new[] { "ParentFolderId", "Name" },
                unique: true);
        }
    }
}
