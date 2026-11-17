using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SnackCounter.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "offices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    time_zone_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    allow_visitors_to_volunteer = table.Column<bool>(type: "boolean", nullable: false),
                    monthly_budget_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    monthly_budget_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    typical_max_purchase_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    typical_max_purchase_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    typical_min_purchase_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    typical_min_purchase_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_offices", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "budget_periods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    month = table.Column<string>(type: "character(7)", fixedLength: true, maxLength: 7, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    limit_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    limit_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_periods", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_periods_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "counters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_counters", x => x.id);
                    table.ForeignKey(
                        name: "fk_counters_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    hr_employee_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    chat_user_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    home_office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payroll_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    default_office_days_mask = table.Column<int>(type: "integer", nullable: false),
                    is_volunteer = table.Column<bool>(type: "boolean", nullable: false),
                    is_office_admin = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_offices_home_office_id",
                        column: x => x.home_office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "budget_ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    refill_id = table.Column<Guid>(type: "uuid", nullable: true),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    over_budget = table.Column<bool>(type: "boolean", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    budget_period_id = table.Column<Guid>(type: "uuid", nullable: true),
                    amount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_budget_ledger_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_budget_ledger_entries_budget_periods_budget_period_id",
                        column: x => x.budget_period_id,
                        principalTable: "budget_periods",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "availability",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_date = table.Column<DateOnly>(type: "date", nullable: false),
                    to_date = table.Column<DateOnly>(type: "date", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    visiting_office_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_availability", x => x.id);
                    table.ForeignKey(
                        name: "fk_availability_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_availability_offices_visiting_office_id",
                        column: x => x.visiting_office_id,
                        principalTable: "offices",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "refills",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    counter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    office_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    reported_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reported_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assignee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    deadline_utc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    committed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reminder_sent = table.Column<bool>(type: "boolean", nullable: false),
                    purchase = table.Column<string>(type: "jsonb", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refills", x => x.id);
                    table.ForeignKey(
                        name: "fk_refills_counters_counter_id",
                        column: x => x.counter_id,
                        principalTable: "counters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_refills_employees_assignee_id",
                        column: x => x.assignee_id,
                        principalTable: "employees",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_refills_employees_reported_by",
                        column: x => x.reported_by,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_refills_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "assignment_attempts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    offered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    explanation = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    refill_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignment_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignment_attempts_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_assignment_attempts_refills_refill_id",
                        column: x => x.refill_id,
                        principalTable: "refills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "claims",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    refill_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    office_id = table.Column<Guid>(type: "uuid", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    receipt_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hr_claim_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    resubmission_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    overdue_flagged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    amount_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    amount_currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_claims_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_claims_offices_office_id",
                        column: x => x.office_id,
                        principalTable: "offices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_claims_refills_refill_id",
                        column: x => x.refill_id,
                        principalTable: "refills",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_assignment_attempts_employee_id",
                table: "assignment_attempts",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignment_attempts_refill_id",
                table: "assignment_attempts",
                column: "refill_id");

            migrationBuilder.CreateIndex(
                name: "ix_availability_employee_id_from_date",
                table: "availability",
                columns: new[] { "employee_id", "from_date" });

            migrationBuilder.CreateIndex(
                name: "ix_availability_visiting_office_id",
                table: "availability",
                column: "visiting_office_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_ledger_entries_budget_period_id",
                table: "budget_ledger_entries",
                column: "budget_period_id");

            migrationBuilder.CreateIndex(
                name: "ix_budget_ledger_entries_refill_id",
                table: "budget_ledger_entries",
                column: "refill_id");

            migrationBuilder.CreateIndex(
                name: "ux_budget_period",
                table: "budget_periods",
                columns: new[] { "office_id", "month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_claims_employee_id_status",
                table: "claims",
                columns: new[] { "employee_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_claims_office_id",
                table: "claims",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "ix_claims_refill_id",
                table: "claims",
                column: "refill_id");

            migrationBuilder.CreateIndex(
                name: "ix_counters_office_id",
                table: "counters",
                column: "office_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_chat_user_id",
                table: "employees",
                column: "chat_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_home_office_id",
                table: "employees",
                column: "home_office_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_hr_employee_id",
                table: "employees",
                column: "hr_employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_due",
                table: "outbox_messages",
                column: "next_attempt_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_refills_assignee_id",
                table: "refills",
                column: "assignee_id");

            migrationBuilder.CreateIndex(
                name: "ix_refills_due",
                table: "refills",
                column: "deadline_utc",
                filter: "status IN ('Offered','Committed')");

            migrationBuilder.CreateIndex(
                name: "ix_refills_office_id_status",
                table: "refills",
                columns: new[] { "office_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_refills_reported_by",
                table: "refills",
                column: "reported_by");

            migrationBuilder.CreateIndex(
                name: "ux_refills_one_active_per_counter",
                table: "refills",
                column: "counter_id",
                unique: true,
                filter: "status IN ('Open','Offered','Escalated','OnHold','Committed')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_attempts");

            migrationBuilder.DropTable(
                name: "availability");

            migrationBuilder.DropTable(
                name: "budget_ledger_entries");

            migrationBuilder.DropTable(
                name: "claims");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "budget_periods");

            migrationBuilder.DropTable(
                name: "refills");

            migrationBuilder.DropTable(
                name: "counters");

            migrationBuilder.DropTable(
                name: "employees");

            migrationBuilder.DropTable(
                name: "offices");
        }
    }
}
