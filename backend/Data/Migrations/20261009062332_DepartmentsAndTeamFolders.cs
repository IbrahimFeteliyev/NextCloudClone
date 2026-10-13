using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class DepartmentsAndTeamFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DepartmentId",
                table: "Users",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTeamRoot",
                table: "Folders",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamSpaceId",
                table: "Folders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TeamSpaceId",
                table: "Files",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Departments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Departments", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TeamSpaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamSpaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Groups",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    DepartmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsAllStaff = table.Column<bool>(type: "boolean", nullable: false),
                    IsAdministrators = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Groups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Groups_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GroupPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false),
                    FolderId = table.Column<Guid>(type: "uuid", nullable: true),
                    FileId = table.Column<Guid>(type: "uuid", nullable: true),
                    Access = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupPermissions", x => x.Id);
                    table.CheckConstraint("CK_GroupPermissions_Resource", "(\"FolderId\" IS NULL) <> (\"FileId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_GroupPermissions_Files_FileId",
                        column: x => x.FileId,
                        principalTable: "Files",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupPermissions_Folders_FolderId",
                        column: x => x.FolderId,
                        principalTable: "Folders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupPermissions_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserGroups",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GroupId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserGroups", x => new { x.UserId, x.GroupId });
                    table.ForeignKey(
                        name: "FK_UserGroups_Groups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "Groups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserGroups_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_DepartmentId",
                table: "Users",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Folders_TeamSpaceId",
                table: "Folders",
                column: "TeamSpaceId",
                unique: true,
                filter: "\"IsTeamRoot\" = true");

            migrationBuilder.CreateIndex(
                name: "IX_Files_TeamSpaceId",
                table: "Files",
                column: "TeamSpaceId");

            migrationBuilder.CreateIndex(
                name: "IX_Departments_Name",
                table: "Departments",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPermissions_FileId",
                table: "GroupPermissions",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupPermissions_FolderId",
                table: "GroupPermissions",
                column: "FolderId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupPermissions_GroupId_FileId",
                table: "GroupPermissions",
                columns: new[] { "GroupId", "FileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupPermissions_GroupId_FolderId",
                table: "GroupPermissions",
                columns: new[] { "GroupId", "FolderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Groups_DepartmentId",
                table: "Groups",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Groups_Name",
                table: "Groups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamSpaces_Name",
                table: "TeamSpaces",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserGroups_GroupId",
                table: "UserGroups",
                column: "GroupId");

            migrationBuilder.AddForeignKey(
                name: "FK_Files_TeamSpaces_TeamSpaceId",
                table: "Files",
                column: "TeamSpaceId",
                principalTable: "TeamSpaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Folders_TeamSpaces_TeamSpaceId",
                table: "Folders",
                column: "TeamSpaceId",
                principalTable: "TeamSpaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users",
                column: "DepartmentId",
                principalTable: "Departments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Files_TeamSpaces_TeamSpaceId",
                table: "Files");

            migrationBuilder.DropForeignKey(
                name: "FK_Folders_TeamSpaces_TeamSpaceId",
                table: "Folders");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Departments_DepartmentId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "GroupPermissions");

            migrationBuilder.DropTable(
                name: "TeamSpaces");

            migrationBuilder.DropTable(
                name: "UserGroups");

            migrationBuilder.DropTable(
                name: "Groups");

            migrationBuilder.DropTable(
                name: "Departments");

            migrationBuilder.DropIndex(
                name: "IX_Users_DepartmentId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Folders_TeamSpaceId",
                table: "Folders");

            migrationBuilder.DropIndex(
                name: "IX_Files_TeamSpaceId",
                table: "Files");

            migrationBuilder.DropColumn(
                name: "DepartmentId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "IsTeamRoot",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "TeamSpaceId",
                table: "Folders");

            migrationBuilder.DropColumn(
                name: "TeamSpaceId",
                table: "Files");
        }
    }
}
