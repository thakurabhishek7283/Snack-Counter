# Snack Counter Automation

Automates the refill of the office snack boxes in Oslo and Bengaluru: who buys, budget checks, and reimbursement through the external HR system.

- **Design:** [docs/03-DESIGN-DOC.md](docs/03-DESIGN-DOC.md) (2 pages) · [HLD](docs/01-HLD.md) · [LLD](docs/02-LLD.md) · [plan and options](docs/00-PLAN.md)
- **Stack:** .NET 10, EF Core 10 + PostgreSQL 17, Minimal APIs, Angular 20 (standalone, signals, zoneless).

## Run it

**Everything in Docker (one command):**

```bash
docker compose up --build      # then open http://localhost:8080
```

This starts PostgreSQL, the API (migrates and seeds demo data on startup, background jobs running) and the web app behind nginx. Stop it with `docker compose down`, or `docker compose down -v` to also wipe the database.

**For development: API with no setup (in-memory storage, demo data):**

```bash
dotnet run --project src/SnackCounter.Api --launch-profile in-memory
```

**With PostgreSQL:**

```bash
docker compose up -d db
dotnet run --project src/SnackCounter.Api --launch-profile postgres   # applies migrations and seeds demo data
```

**The web app** (in a second terminal, with the API running):

```bash
cd web/snack-counter-ui
npm install
npx ng serve        # http://localhost:4200, proxies /api to the API
```

Pick who you are in the header ("Signed in as"), or open `http://localhost:4200/?as=<employee id>`. The **Demo** page plays the outside world: the chat tool's messages with clickable buttons, HR's inbox (approve, reject, pay), a clock you can move forward to show timeouts, and buttons to run the background jobs now.

A 10-minute walkthrough: report the Oslo box empty, accept it in the chat feed, move the clock forward 2 hours to show a hand-off, upload a receipt on the refill page, approve and pay in the HR inbox, then open the report.

The API listens on `http://localhost:5252`. The OpenAPI document is at `/openapi/v1.json`. Say who you are with the `X-Employee-Id` header (demo stand-in for company SSO). For example, Lars in Oslo:

```bash
curl -X POST -H "X-Employee-Id: 0e000000-0000-0000-0001-000000000002" \
     http://localhost:5252/api/counters/0c000000-0000-0000-0000-000000000001/refills
```

Demo ids are in [DemoData.cs](src/SnackCounter.Infrastructure/Seed/DemoData.cs). Dev-only endpoints under `/api/dev` let you see the simulated chat messages and HR claims, approve, reject or pay claims as HR, move the clock forward, and run background jobs on demand.

## Run the tests

```bash
dotnet test                                   # 110 .NET tests
cd web/snack-counter-ui && npx ng test --watch=false   # 33 UI tests (Vitest)
```

| Project | Count | What |
|---|---|---|
| `SnackCounter.Domain.Tests` | 74 | Every rule in isolation. Pure, millisecond-fast |
| `SnackCounter.Application.Tests` | 23 | Use cases end to end through DI, with in-memory storage, simulated chat and HR, and a fake clock |
| `SnackCounter.Integration.Tests` | 13 | Real PostgreSQL in Docker (Testcontainers) and the real API over HTTP: concurrency, constraints, mapping, the full lifecycle. **Skipped automatically if Docker isn't running** |
| `web/snack-counter-ui` | 33 | Formatting, status wording, who sees which button, budget strip, identity header and error messages |

Known demo limitation: receipt images are kept in memory, so they're lost when the API restarts. Claims and money are stored in PostgreSQL. A blob-storage `IReceiptStore` is the production swap.

## Layout

```
src/
  SnackCounter.Domain/          pure C#, no dependencies: aggregates, rules, value objects
  SnackCounter.Application/     one folder per use case, ports (interfaces), notifications
  SnackCounter.Infrastructure/  EF Core + PostgreSQL, in-memory store, simulated chat + HR, FX, receipts, demo data
  SnackCounter.Api/             Minimal API endpoints, background jobs, dev tools
tests/
  SnackCounter.Domain.Tests/        rules in isolation
  SnackCounter.Application.Tests/   end-to-end scenarios (TestApp harness)
  SnackCounter.Integration.Tests/   PostgreSQL + HTTP
web/snack-counter-ui/            Angular 20 web app
docs/                            plan, HLD, LLD, design doc
```

## Where the core logic lives

| Concern | Code |
|---|---|
| Refill lifecycle (state machine) | [RefillTransitions.cs](src/SnackCounter.Domain/Refills/RefillTransitions.cs), [Refill.cs](src/SnackCounter.Domain/Refills/Refill.cs) |
| Choosing who buys | [AssignmentEngine.cs](src/SnackCounter.Domain/Assignment/AssignmentEngine.cs), [EligibilityRules.cs](src/SnackCounter.Domain/Assignment/Rules/EligibilityRules.cs), [ScoringRules.cs](src/SnackCounter.Domain/Assignment/Scorers/ScoringRules.cs) |
| Budget check | [BudgetPeriod.cs](src/SnackCounter.Domain/Budgeting/BudgetPeriod.cs), [BudgetPolicy.cs](src/SnackCounter.Domain/Budgeting/BudgetPolicy.cs) |
| Money and currencies | [Money.cs](src/SnackCounter.Domain/Common/Money.cs), [BudgetMonth.cs](src/SnackCounter.Domain/Common/BudgetMonth.cs), [MonthlySpendReport.cs](src/SnackCounter.Domain/Reporting/MonthlySpendReport.cs) |
| Reimbursement | [Claim.cs](src/SnackCounter.Domain/Reimbursement/Claim.cs), [ClaimFeatures.cs](src/SnackCounter.Application/Features/Claims/ClaimFeatures.cs) |
| Use cases | [Features/](src/SnackCounter.Application/Features/) |
| Persistence and concurrency | [Configurations.cs](src/SnackCounter.Infrastructure/Persistence/Configurations.cs), [EfUnitOfWork.cs](src/SnackCounter.Infrastructure/Persistence/EfUnitOfWork.cs), [migrations](src/SnackCounter.Infrastructure/Persistence/Migrations/) |
| HTTP API | [Endpoints/](src/SnackCounter.Api/Endpoints/), [Program.cs](src/SnackCounter.Api/Program.cs) |
| Simulated chat and HR | [SimulatedChatGateway.cs](src/SnackCounter.Infrastructure/Simulated/SimulatedChatGateway.cs), [SimulatedHrGateway.cs](src/SnackCounter.Infrastructure/Simulated/SimulatedHrGateway.cs) |

## The important rules, and the tests that prove them

| Rule | Tests |
|---|---|
| Two people cannot both commit to the same refill | `Second_person_cannot_commit_once_someone_has_committed`, `Two_volunteers_cannot_both_commit_to_the_same_refill`, `Concurrent_commits_on_the_same_refill_are_detected` (PostgreSQL), `Ten_volunteers_racing_to_accept_exactly_one_wins` (HTTP + PostgreSQL) |
| No duplicate refills for one box | `Second_empty_report_returns_the_existing_refill`, `Database_rejects_a_second_active_refill_for_the_same_box`, `Simultaneous_empty_reports_create_one_refill` |
| A refill nobody completes is handed to someone else | `Unanswered_offer_is_handed_to_the_next_volunteer_after_the_ttl`, `Missed_purchase_deadline_releases_the_budget_and_hands_the_refill_on`, `After_three_declines_the_refill_is_broadcast_and_anyone_can_take_it`, `Unanswered_offer_moves_to_the_next_volunteer_after_two_hours` (HTTP + PostgreSQL) |
| Budgets and totals are correct across both currencies | `Monthly_report_totals_both_offices_in_one_currency`, `Visitor_from_Bengaluru_buys_in_Oslo_charged_to_Oslo_in_NOK`, `Purchase_after_midnight_on_the_first_counts_toward_the_new_month`, `Concurrent_reservations_on_the_same_budget_are_detected` |
| Fair distribution of work and money | `Prefers_the_volunteer_with_the_fewest_recent_refills`, `Volunteer_already_owed_two_claims_is_skipped_for_the_next_refill` |
| Holiday, remote and visiting are respected | `Excludes_people_on_holiday_or_working_remotely`, `Declaring_a_holiday_in_chat_removes_you_from_the_rota` |
| Claims reach HR exactly once and are chased | `Claim_reaches_HR_exactly_once_even_when_HR_is_down_at_first`, `Claim_waiting_more_than_ten_days_is_flagged_to_the_admin_once` |

## Status

| Phase | State |
|---|---|
| P0 Domain core + unit tests | Done |
| P1 Use cases, in-memory persistence, simulated chat/HR, scenario tests | Done |
| P2 PostgreSQL (EF Core), outbox table, Minimal API, background jobs, Testcontainers tests | Done |
| P3 Angular 20 UI with demo panel (chat feed, HR approve/reject/pay, clock, jobs) | Done |
| P4 Containerised API + web app (docker compose up), UI unit tests | Done |
