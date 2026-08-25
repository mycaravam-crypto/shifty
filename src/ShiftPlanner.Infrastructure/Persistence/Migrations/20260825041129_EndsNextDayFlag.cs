using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EndsNextDayFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EndsNextDay",
                table: "ShiftAssignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndsNextDay",
                table: "ShiftAssignments");
        }
    }
}
