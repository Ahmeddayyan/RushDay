using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RushDay.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// The one v1 migration (01-domain-and-data.md section 5). Generated with dotnet ef, then hand-edited so it is
    /// valid on the populated database: every NOT NULL column added to an existing table gets a migration-time
    /// default that is dropped afterwards unless the model keeps it, modules.department is derived from the code,
    /// and audit_events is made append-only by a trigger. It contains no enrolled_count reconciliation: the count
    /// is year-scoped and needs the academic_settings row, so the last startup backfill computes it.
    /// </summary>
    public partial class PortalAndIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Identity tables (ASP.NET Core Identity's default shape, renamed) and the Data Protection key ring.
            //    Foreign keys from users to students and lecturers are added in step 7, after lecturers exists.
            migrationBuilder.CreateTable(
                name: "roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    lecturer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_demo = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    disabled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                    table.CheckConstraint("ck_users_one_principal", "NOT (student_id IS NOT NULL AND lecturer_id IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "users",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_lecturer_id",
                table: "users",
                column: "lecturer_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_student_id",
                table: "users",
                column: "student_id",
                unique: true);

            migrationBuilder.CreateTable(
                name: "user_roles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateTable(
                name: "user_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateTable(
                name: "role_claims",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_roles_role_id",
                        column: x => x.role_id,
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateTable(
                name: "user_logins",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateTable(
                name: "user_tokens",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "data_protection_keys",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    friendly_name = table.Column<string>(type: "text", nullable: true),
                    xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_protection_keys", x => x.id);
                });

            // 2. New domain tables. Their foreign keys are added in step 7.
            migrationBuilder.CreateTable(
                name: "lecturers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    staff_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    title = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    department = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    left_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lecturers", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lecturers_staff_number",
                table: "lecturers",
                column: "staff_number",
                unique: true);

            migrationBuilder.CreateTable(
                name: "module_lecturers",
                columns: table => new
                {
                    module_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lecturer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_lecturers", x => new { x.module_id, x.lecturer_id });
                    table.CheckConstraint("ck_module_lecturers_role", "role IN ('Leader', 'Teacher')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_module_lecturers_assigned_by_user_id",
                table: "module_lecturers",
                column: "assigned_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_module_lecturers_lecturer_id",
                table: "module_lecturers",
                column: "lecturer_id");

            migrationBuilder.CreateTable(
                name: "academic_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    institution_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    institution_short_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    current_semester = table.Column<int>(type: "integer", nullable: false),
                    support_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    support_url = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_academic_settings", x => x.id);
                    table.CheckConstraint("ck_academic_settings_current_semester", "current_semester IN (1, 2)");
                    table.CheckConstraint("ck_academic_settings_singleton", "id = 1");
                });

            migrationBuilder.CreateIndex(
                name: "ix_academic_settings_updated_by_user_id",
                table: "academic_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateTable(
                name: "enrolment_windows",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    semester = table.Column<int>(type: "integer", nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    withdrawal_deadline_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrolment_windows", x => x.id);
                    table.CheckConstraint("ck_enrolment_windows_order", "opens_at < closes_at AND closes_at <= withdrawal_deadline_at");
                    table.CheckConstraint("ck_enrolment_windows_semester", "semester IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrolment_windows_academic_year_semester",
                table: "enrolment_windows",
                columns: new[] { "academic_year", "semester" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_enrolment_windows_created_by_user_id",
                table: "enrolment_windows",
                column: "created_by_user_id");

            migrationBuilder.CreateTable(
                name: "results_publications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    academic_year = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    semester = table.Column<int>(type: "integer", nullable: false),
                    publish_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    grade_count = table.Column<int>(type: "integer", nullable: false),
                    module_count = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_results_publications", x => x.id);
                    table.CheckConstraint("ck_results_publications_semester", "semester IN (1, 2)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_results_publications_created_by_user_id",
                table: "results_publications",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_results_publications_semester_created_at",
                table: "results_publications",
                columns: new[] { "academic_year", "semester", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateTable(
                name: "announcements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    scope = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    module_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    body = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    pinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_announcements", x => x.id);
                    table.CheckConstraint("ck_announcements_scope", "scope IN ('University', 'Module')");
                    table.CheckConstraint("ck_announcements_scope_module", "(scope = 'Module') = (module_id IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_created_by_user_id",
                table: "announcements",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_announcements_module_id_published_at",
                table: "announcements",
                columns: new[] { "module_id", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_announcements_scope_published_at",
                table: "announcements",
                columns: new[] { "scope", "published_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateTable(
                name: "audit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    subject_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    subject_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    student_id = table.Column<Guid>(type: "uuid", nullable: true),
                    module_id = table.Column<Guid>(type: "uuid", nullable: true),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    request_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    ip_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    chain_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                });

            // Immutability is enforced by PostgreSQL, not by convention: the application role, a raw-SQL mistake or
            // an injected statement cannot rewrite or delete the trail (01-domain-and-data.md section 3).
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_events_immutable() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN RAISE EXCEPTION 'audit_events is append-only'; END $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_audit_events_immutable BEFORE UPDATE OR DELETE ON audit_events
                  FOR EACH ROW EXECUTE FUNCTION audit_events_immutable();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_action_occurred_at",
                table: "audit_events",
                columns: new[] { "action", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_actor_user_id_occurred_at",
                table: "audit_events",
                columns: new[] { "actor_user_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_module_id_occurred_at",
                table: "audit_events",
                columns: new[] { "module_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_occurred_at",
                table: "audit_events",
                column: "occurred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_audit_events_student_id_occurred_at",
                table: "audit_events",
                columns: new[] { "student_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateTable(
                name: "data_backfills",
                columns: table => new
                {
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rows_affected = table.Column<int>(type: "integer", nullable: false),
                    notes = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_data_backfills", x => x.name);
                });

            // 3. students
            migrationBuilder.AddColumn<string>(
                name: "email",
                table: "students",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "left_at",
                table: "students",
                type: "timestamp with time zone",
                nullable: true);

            // 4. modules. department is added with a default so the populated table migrates in one statement,
            //    filled from the code, and the default is dropped (the model carries none). enrolled_count and
            //    is_active keep their defaults (the model has HasDefaultValue for both).
            migrationBuilder.AddColumn<string>(
                name: "department",
                table: "modules",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "modules",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "enrolled_count",
                table: "modules",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "modules",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "modules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE modules SET department = left(code, 2)");
            migrationBuilder.Sql("ALTER TABLE modules ALTER COLUMN department DROP DEFAULT");

            migrationBuilder.AddCheckConstraint(
                name: "ck_modules_capacity_positive",
                table: "modules",
                sql: "capacity >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_modules_enrolled_count_non_negative",
                table: "modules",
                sql: "enrolled_count >= 0");

            // 5. enrolments. status and source keep their defaults (model HasDefaultValue; the 'Seed' default lets
            //    an insert by the v0 container during Render's deploy overlap succeed). academic_year's default
            //    exists only here: every seeded row is a 2025/26 autumn enrolment; the v0 load-run rows on CS3099
            //    are relabelled 2026/27 by backfill step 4.
            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "enrolments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "enrolments",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Seed");

            migrationBuilder.AddColumn<string>(
                name: "academic_year",
                table: "enrolments",
                type: "character varying(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "2025/26");

            migrationBuilder.Sql("ALTER TABLE enrolments ALTER COLUMN academic_year DROP DEFAULT");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "withdrawn_at",
                table: "enrolments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_user_id",
                table: "enrolments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "enrolments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "ix_enrolments_module_id",
                table: "enrolments");

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_module_id_status",
                table: "enrolments",
                columns: new[] { "module_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_student_id_academic_year_status",
                table: "enrolments",
                columns: new[] { "student_id", "academic_year", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_created_by_user_id",
                table: "enrolments",
                column: "created_by_user_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_enrolments_source",
                table: "enrolments",
                sql: "source IN ('Seed', 'Self', 'Admin')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_enrolments_status",
                table: "enrolments",
                sql: "status IN ('Active', 'Withdrawn')");

            // 6. grades. Every existing row is a published mark, so status defaults to 'Published' for the
            //    migration only; updated_at defaults to now() for the migration only; outcome ('Mark') and
            //    version (1) keep their defaults; mark becomes nullable (null iff outcome <> 'Mark').
            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "published_at",
                table: "grades",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<int>(
                name: "mark",
                table: "grades",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<string>(
                name: "outcome",
                table: "grades",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Mark");

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "grades",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "Published");

            migrationBuilder.Sql("ALTER TABLE grades ALTER COLUMN status DROP DEFAULT");

            migrationBuilder.AddColumn<Guid>(
                name: "publication_id",
                table: "grades",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "entered_by_user_id",
                table: "grades",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "submitted_at",
                table: "grades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "grades",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "now()");

            migrationBuilder.Sql("ALTER TABLE grades ALTER COLUMN updated_at DROP DEFAULT");

            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "grades",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "corrected_at",
                table: "grades",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.DropIndex(
                name: "ix_grades_module_id",
                table: "grades");

            migrationBuilder.CreateIndex(
                name: "ix_grades_student_id_status_published_at",
                table: "grades",
                columns: new[] { "student_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_grades_module_id_status",
                table: "grades",
                columns: new[] { "module_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_grades_entered_by_user_id",
                table: "grades",
                column: "entered_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_grades_publication_id",
                table: "grades",
                column: "publication_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grades_status",
                table: "grades",
                sql: "status IN ('Draft', 'Submitted', 'Published')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grades_outcome",
                table: "grades",
                sql: "outcome IN ('Mark', 'Absent', 'Deferred')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grades_mark_range",
                table: "grades",
                sql: "mark IS NULL OR (mark >= 0 AND mark <= 100)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grades_mark_outcome",
                table: "grades",
                sql: "(outcome = 'Mark') = (mark IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_grades_published_has_instant",
                table: "grades",
                sql: "status <> 'Published' OR published_at IS NOT NULL");

            // 7. Foreign keys. Users are never deleted, so everything that points at users is RESTRICT;
            //    child rows of modules and lecturers cascade as the v0 tables already do.
            migrationBuilder.AddForeignKey(
                name: "fk_users_students_student_id",
                table: "users",
                column: "student_id",
                principalTable: "students",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_users_lecturers_lecturer_id",
                table: "users",
                column: "lecturer_id",
                principalTable: "lecturers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_module_lecturers_modules_module_id",
                table: "module_lecturers",
                column: "module_id",
                principalTable: "modules",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_module_lecturers_lecturers_lecturer_id",
                table: "module_lecturers",
                column: "lecturer_id",
                principalTable: "lecturers",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_module_lecturers_users_assigned_by_user_id",
                table: "module_lecturers",
                column: "assigned_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_enrolments_users_created_by_user_id",
                table: "enrolments",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_grades_results_publications_publication_id",
                table: "grades",
                column: "publication_id",
                principalTable: "results_publications",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_grades_users_entered_by_user_id",
                table: "grades",
                column: "entered_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_announcements_modules_module_id",
                table: "announcements",
                column: "module_id",
                principalTable: "modules",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_announcements_users_created_by_user_id",
                table: "announcements",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_academic_settings_users_updated_by_user_id",
                table: "academic_settings",
                column: "updated_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_enrolment_windows_users_created_by_user_id",
                table: "enrolment_windows",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_results_publications_users_created_by_user_id",
                table: "results_publications",
                column: "created_by_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 7. Foreign keys
            migrationBuilder.DropForeignKey(name: "fk_results_publications_users_created_by_user_id", table: "results_publications");
            migrationBuilder.DropForeignKey(name: "fk_enrolment_windows_users_created_by_user_id", table: "enrolment_windows");
            migrationBuilder.DropForeignKey(name: "fk_academic_settings_users_updated_by_user_id", table: "academic_settings");
            migrationBuilder.DropForeignKey(name: "fk_announcements_users_created_by_user_id", table: "announcements");
            migrationBuilder.DropForeignKey(name: "fk_announcements_modules_module_id", table: "announcements");
            migrationBuilder.DropForeignKey(name: "fk_grades_users_entered_by_user_id", table: "grades");
            migrationBuilder.DropForeignKey(name: "fk_grades_results_publications_publication_id", table: "grades");
            migrationBuilder.DropForeignKey(name: "fk_enrolments_users_created_by_user_id", table: "enrolments");
            migrationBuilder.DropForeignKey(name: "fk_module_lecturers_users_assigned_by_user_id", table: "module_lecturers");
            migrationBuilder.DropForeignKey(name: "fk_module_lecturers_lecturers_lecturer_id", table: "module_lecturers");
            migrationBuilder.DropForeignKey(name: "fk_module_lecturers_modules_module_id", table: "module_lecturers");
            migrationBuilder.DropForeignKey(name: "fk_users_lecturers_lecturer_id", table: "users");
            migrationBuilder.DropForeignKey(name: "fk_users_students_student_id", table: "users");

            // 6. grades. Existing v0 rows keep their published_at and mark; only rows that gained a null instant or
            //    a null mark (drafts, submissions, absences and deferrals created after the upgrade) are stamped
            //    before NOT NULL returns.
            migrationBuilder.DropCheckConstraint(name: "ck_grades_published_has_instant", table: "grades");
            migrationBuilder.DropCheckConstraint(name: "ck_grades_mark_outcome", table: "grades");
            migrationBuilder.DropCheckConstraint(name: "ck_grades_mark_range", table: "grades");
            migrationBuilder.DropCheckConstraint(name: "ck_grades_outcome", table: "grades");
            migrationBuilder.DropCheckConstraint(name: "ck_grades_status", table: "grades");
            migrationBuilder.DropIndex(name: "ix_grades_publication_id", table: "grades");
            migrationBuilder.DropIndex(name: "ix_grades_entered_by_user_id", table: "grades");
            migrationBuilder.DropIndex(name: "ix_grades_module_id_status", table: "grades");
            migrationBuilder.DropIndex(name: "ix_grades_student_id_status_published_at", table: "grades");

            migrationBuilder.CreateIndex(
                name: "ix_grades_module_id",
                table: "grades",
                column: "module_id");

            migrationBuilder.DropColumn(name: "corrected_at", table: "grades");
            migrationBuilder.DropColumn(name: "version", table: "grades");
            migrationBuilder.DropColumn(name: "updated_at", table: "grades");
            migrationBuilder.DropColumn(name: "submitted_at", table: "grades");
            migrationBuilder.DropColumn(name: "entered_by_user_id", table: "grades");
            migrationBuilder.DropColumn(name: "publication_id", table: "grades");
            migrationBuilder.DropColumn(name: "status", table: "grades");
            migrationBuilder.DropColumn(name: "outcome", table: "grades");

            migrationBuilder.Sql("UPDATE grades SET published_at = now() WHERE published_at IS NULL");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "published_at",
                table: "grades",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.Sql("UPDATE grades SET mark = 0 WHERE mark IS NULL");

            migrationBuilder.AlterColumn<int>(
                name: "mark",
                table: "grades",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // 5. enrolments
            migrationBuilder.DropCheckConstraint(name: "ck_enrolments_status", table: "enrolments");
            migrationBuilder.DropCheckConstraint(name: "ck_enrolments_source", table: "enrolments");
            migrationBuilder.DropIndex(name: "ix_enrolments_created_by_user_id", table: "enrolments");
            migrationBuilder.DropIndex(name: "ix_enrolments_student_id_academic_year_status", table: "enrolments");
            migrationBuilder.DropIndex(name: "ix_enrolments_module_id_status", table: "enrolments");

            migrationBuilder.CreateIndex(
                name: "ix_enrolments_module_id",
                table: "enrolments",
                column: "module_id");

            migrationBuilder.DropColumn(name: "updated_at", table: "enrolments");
            migrationBuilder.DropColumn(name: "created_by_user_id", table: "enrolments");
            migrationBuilder.DropColumn(name: "withdrawn_at", table: "enrolments");
            migrationBuilder.DropColumn(name: "academic_year", table: "enrolments");
            migrationBuilder.DropColumn(name: "source", table: "enrolments");
            migrationBuilder.DropColumn(name: "status", table: "enrolments");

            // 4. modules
            migrationBuilder.DropCheckConstraint(name: "ck_modules_enrolled_count_non_negative", table: "modules");
            migrationBuilder.DropCheckConstraint(name: "ck_modules_capacity_positive", table: "modules");
            migrationBuilder.DropColumn(name: "updated_at", table: "modules");
            migrationBuilder.DropColumn(name: "is_active", table: "modules");
            migrationBuilder.DropColumn(name: "enrolled_count", table: "modules");
            migrationBuilder.DropColumn(name: "description", table: "modules");
            migrationBuilder.DropColumn(name: "department", table: "modules");

            // 3. students
            migrationBuilder.DropColumn(name: "left_at", table: "students");
            migrationBuilder.DropColumn(name: "email", table: "students");

            // 2. New domain tables; the trigger and its function go before the table they guard.
            migrationBuilder.DropTable(name: "data_backfills");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS trg_audit_events_immutable ON audit_events;");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_events_immutable();");
            migrationBuilder.DropTable(name: "audit_events");
            migrationBuilder.DropTable(name: "announcements");
            migrationBuilder.DropTable(name: "results_publications");
            migrationBuilder.DropTable(name: "enrolment_windows");
            migrationBuilder.DropTable(name: "academic_settings");
            migrationBuilder.DropTable(name: "module_lecturers");
            migrationBuilder.DropTable(name: "lecturers");

            // 1. Identity tables
            migrationBuilder.DropTable(name: "data_protection_keys");
            migrationBuilder.DropTable(name: "user_tokens");
            migrationBuilder.DropTable(name: "user_logins");
            migrationBuilder.DropTable(name: "role_claims");
            migrationBuilder.DropTable(name: "user_claims");
            migrationBuilder.DropTable(name: "user_roles");
            migrationBuilder.DropTable(name: "users");
            migrationBuilder.DropTable(name: "roles");
        }
    }
}
