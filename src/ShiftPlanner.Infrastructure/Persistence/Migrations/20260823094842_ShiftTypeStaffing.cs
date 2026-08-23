using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ShiftTypeStaffing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxStaffing",
                table: "ShiftTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinStaffing",
                table: "ShiftTypes",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxStaffing",
                table: "ShiftTypes");

            migrationBuilder.DropColumn(
                name: "MinStaffing",
                table: "ShiftTypes");
        }
    }
}
