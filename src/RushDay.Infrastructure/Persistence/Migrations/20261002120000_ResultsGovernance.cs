using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RushDay.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The second v1 migration (01-domain-and-data.md section 5a; review S6 E2 and E10), additive and small, so the
    /// rehearsed <c>PortalAndIdentity</c> stays exactly as it was:
    /// <list type="number">
    /// <item><c>results_publications.announcement_id</c> (nullable, FK to <c>announcements</c> ON DELETE SET NULL, indexed):
    /// the pinned "results are available" announcement a publish with <c>announce</c> posted, so a reschedule moves it
    /// and a cancel, an unpublish or an emptying return to draft deletes it.</item>
    /// <item><c>ix_module_lecturers_module_id_leader</c>, a unique index on <c>module_lecturers(module_id)</c> WHERE
    /// <c>role = 'Leader'</c>: at most one leader per module, whatever two concurrent assignments do. A database that
    /// already holds a module with two leaders (the race the index closes) keeps the leader assigned first and demotes
    /// the others to <c>Teacher</c> before the index is built; no row is deleted.</item>
    /// </list>
    /// Every statement is DDL or an UPDATE of a handful of rows; nothing touches the 80,000-row tables.
    /// </summary>
    public partial class ResultsGovernance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "announcement_id",
                table: "results_publications",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_results_publications_announcement_id",
                table: "results_publications",
                column: "announcement_id");

            // One leader per module: the earliest assignment (then the lowest lecturer id) stays leader.
            migrationBuilder.Sql(
                """
                UPDATE module_lecturers ml SET role = 'Teacher'
                WHERE ml.role = 'Leader'
                  AND EXISTS (
                      SELECT 1 FROM module_lecturers o
                      WHERE o.module_id = ml.module_id AND o.role = 'Leader'
                        AND (o.assigned_at, o.lecturer_id) < (ml.assigned_at, ml.lecturer_id));
                """);

            migrationBuilder.CreateIndex(
                name: "ix_module_lecturers_module_id_leader",
                table: "module_lecturers",
                column: "module_id",
                unique: true,
                filter: "role = 'Leader'");

            migrationBuilder.AddForeignKey(
                name: "fk_results_publications_announcements_announcement_id",
                table: "results_publications",
                column: "announcement_id",
                principalTable: "announcements",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_results_publications_announcements_announcement_id",
                table: "results_publications");

            migrationBuilder.DropIndex(
                name: "ix_results_publications_announcement_id",
                table: "results_publications");

            migrationBuilder.DropIndex(
                name: "ix_module_lecturers_module_id_leader",
                table: "module_lecturers");

            migrationBuilder.DropColumn(
                name: "announcement_id",
                table: "results_publications");
        }
    }
}
