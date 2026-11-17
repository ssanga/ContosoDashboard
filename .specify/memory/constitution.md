# ContosoDashboard Constitution

ContosoDashboard is a fictional application built for TRAINING PURPOSES ONLY to teach
Spec-Driven Development (SDD) with GitHub Spec Kit. These principles govern all specifications,
plans, tasks and code produced for it.

## Core Principles

### I. Offline-First, Local-Only (NON-NEGOTIABLE)
The application MUST run fully offline on a developer machine, with no cloud subscription,
external service or network dependency. Data MUST live in SQLite (local file) and uploaded files
MUST live on the local filesystem. No Azure SDK or other cloud SDK package may be referenced in
the training implementation.
Rationale: training must be available to every student without cost, accounts or connectivity.

### II. Infrastructure Abstraction for Cloud Migration
Every infrastructure dependency (file storage, identity, database provider) MUST be reached through
an interface registered with dependency injection, so that a local implementation can be replaced
by a cloud one (e.g. `IFileStorageService`: `LocalFileStorageService` now, `AzureBlobStorageService`
later) without changing business logic, UI or database schema. Persisted paths MUST be relative and
portable between local paths and blob names.
Rationale: students learn the abstraction pattern and migration needs configuration, not rewrites.

### III. Security by Default, Defense in Depth
Every protected capability MUST be enforced at three levels: authentication middleware, the
`[Authorize]` attribute on pages, and authorization checks inside the service layer. Services MUST
verify that the current user may access the requested object (IDOR protection) before touching
data; users MUST see only data they are authorized for, based on role and project membership.
User input MUST be validated server-side, user-supplied file names MUST NOT be used in file paths,
and files MUST be served only through endpoints that perform authorization checks.
Rationale: security is the main teaching topic and UI-only checks are not security.

### IV. Layered Architecture with Clear Separation of Concerns
Code MUST be organized as Models (entities), Data (`ApplicationDbContext`), Services (business
logic behind `I*Service` interfaces) and Pages/Shared (Blazor Server UI). Business rules and
authorization MUST live in services, not in Razor components. New features MUST follow the existing
architecture; major rewrites are out of scope. Database access MUST be asynchronous and MUST use
eager loading (`.Include()`) to avoid N+1 queries; frequently queried fields MUST be indexed.
Rationale: consistent structure keeps features understandable and testable.

### V. Simplicity and Documented Limitations
Solutions MUST be the simplest that meet the requirement (YAGNI); added complexity MUST be
justified in the plan. Training simplifications (mock authentication, no rate limiting, no audit
logging by default, `EnsureCreated()` instead of migrations) are intentional and MUST remain
documented as known limitations rather than silently hidden or half-implemented. Training-only
shortcuts MUST NOT be presented as production guidance.
Rationale: the codebase demonstrates good practice in a deliberately reduced scope.

## Technology & Platform Constraints

- Runtime and framework: .NET 10 (`net10.0`), ASP.NET Core with Blazor Server and Razor Pages.
- Data: Entity Framework Core with SQLite (`Data Source=ContosoDashboard.db`). The `.db` file is
  never committed to source control.
- Authentication: cookie-based mock login (8-hour sliding expiration) with claims-based identity.
  Claims MUST include NameIdentifier, Name, Email, Role and Department.
- Roles (hierarchical): Employee < TeamLead < ProjectManager < Administrator. New authorization
  rules MUST respect this hierarchy and project membership.
- UI: Bootstrap 5.3 with Bootstrap Icons.
- Security headers (CSP, X-Frame-Options, etc.) MUST NOT be weakened without a documented reason.
- File uploads: store outside `wwwroot`, generate a unique GUID-based path before the database
  insert, save the file before saving its metadata, validate extension against an allow-list and
  size against the limit, and use the sequence validate -> authorize -> save file -> save record.
- Blazor file inputs MUST copy `IBrowserFile` to a `MemoryStream` immediately and clear the
  reference afterwards.

## Development Workflow & Quality Gates

- Work follows Spec-Driven Development: constitution -> specify -> (clarify) -> plan -> tasks ->
  (analyze/checklist) -> implement. Code MUST trace back to an approved spec and task.
- Every spec MUST define independently testable acceptance scenarios; every plan MUST include a
  Constitution Check against these principles before design and after design.
- The solution MUST restore, build without errors and start (`dotnet restore`, `dotnet build`,
  `dotnet run`) before a change is considered complete, and the affected user flows MUST be
  verified for at least two different roles, including an unauthorized-access attempt.
- Where an automated test project exists, new service logic SHOULD be covered by tests.
- Commits MUST use conventional-style messages (e.g. `feat:`, `docs:`, `chore:`).

## Governance

This constitution supersedes other practices for this repository. Amendments MUST be made through
the `/speckit-constitution` command, documented with a Sync Impact Report, and applied together
with any required follow-up changes to specs, plans or tasks. Versioning follows semantic
versioning: MAJOR for incompatible removals or redefinitions of principles, MINOR for new
principles/sections or materially expanded guidance, PATCH for clarifications. Every plan and
review MUST verify compliance with these principles; deviations MUST be justified in the plan's
complexity tracking and approved before implementation. Runtime guidance is in `README.md`.

**Version**: 1.0.0 | **Ratified**: 2026-11-17 | **Last Amended**: 2026-11-17
