using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmployeePreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PhoneNumber",
                table: "Employees",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ShiftTypePreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    ShiftTypeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftTypePreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftTypePreferences_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ShiftTypePreferences_ShiftTypes_ShiftTypeId",
                        column: x => x.ShiftTypeId,
                        principalTable: "ShiftTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WeekdayPreferences",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EmployeeId = table.Column<Guid>(type: "uuid", nullable: false),
                    DayOfWeek = table.Column<int>(type: "integer", nullable: false),
                    Level = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WeekdayPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WeekdayPreferences_Employees_EmployeeId",
                        column: x => x.EmployeeId,
                        principalTable: "Employees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypePreferences_EmployeeId_ShiftTypeId",
                table: "ShiftTypePreferences",
                columns: new[] { "EmployeeId", "ShiftTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftTypePreferences_ShiftTypeId",
                table: "ShiftTypePreferences",
                column: "ShiftTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_WeekdayPreferences_EmployeeId_DayOfWeek",
                table: "WeekdayPreferences",
                columns: new[] { "EmployeeId", "DayOfWeek" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ShiftTypePreferences");

            migrationBuilder.DropTable(
                name: "WeekdayPreferences");

            migrationBuilder.DropColumn(
                name: "PhoneNumber",
                table: "Employees");
        }
    }
}
