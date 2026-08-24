using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TeamBundesland : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Bundesland",
                table: "Teams",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bundesland",
                table: "Teams");
        }
    }
}
