using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShiftPlanner.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    // issue #156: the model gained a shadow "RowVersion" property mapped onto Postgres's
    // built-in xmin system column (see ApplicationDbContext.OnModelCreating) — xmin already
    // exists on every row of every table, so there is no actual schema change to apply here.
    // EF Core's migration generator doesn't know that and produces an AddColumn/DropColumn
    // "xmin" pair, which Postgres rejects outright (xmin is a reserved system column name you
    // cannot ALTER TABLE ADD/DROP) — hand-edited to a genuine no-op instead of hand-writing the
    // usual DDL, matching this file's own past precedent for editing a generated migration.
    public partial class ShiftAssignmentXminConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
