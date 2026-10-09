using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace backend.Data.Migrations
{
    /// <inheritdoc />
    public partial class OfficeSessionContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InitialObjectKey",
                table: "OfficeSessions",
                type: "text",
                nullable: false,
                defaultValue: "");
            migrationBuilder.Sql("UPDATE \"OfficeSessions\" AS s SET \"InitialObjectKey\" = COALESCE((SELECT v.\"ObjectKey\" FROM \"FileVersions\" AS v WHERE v.\"FileId\" = s.\"FileId\" AND v.\"Number\" = s.\"InitialVersion\"), (SELECT f.\"ObjectKey\" FROM \"Files\" AS f WHERE f.\"Id\" = s.\"FileId\"))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InitialObjectKey",
                table: "OfficeSessions");
        }
    }
}
