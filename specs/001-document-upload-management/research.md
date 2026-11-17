# Phase 0 Research: Document Upload and Management

All technical-context items were resolvable from the repository and the constitution; no
`NEEDS CLARIFICATION` remained. Findings below drive the design.

## Findings in the existing code

- **Login claims**: `Login.cshtml.cs` issues NameIdentifier, Name, Email and Role only. There is **no
  Department claim**, but team-based rules (Team Lead scope, sharing with a team) need the department.
- **Database creation**: `Program.cs` uses `EnsureCreated()`. It creates tables only when the database file
  does not exist, so an existing `ContosoDashboard.db` will **not** receive the new tables.
- **Notifications**: `NotificationType` has no document values; `INotificationService.CreateNotificationAsync`
  is reusable.
- **Authorization style**: pages use `[Authorize]`; services take a `requestingUserId` and return
  null/false/empty when unauthorized. The same style is followed.
- **Security headers**: `X-Frame-Options: DENY` is set globally, so same-site `<iframe>` previews would be blocked.
- **No test project** exists.

## Decisions

### D1. Storage abstraction and layout
- **Decision**: `IFileStorageService` with `LocalFileStorageService` writing under
  `{ContentRoot}/AppData/uploads/`. Stored path (relative, portable):
  `{uploaderId}/{projectId or "personal"}/{guid}.{ext}`. `DocumentService` builds the path **before** the
  database insert and passes it to the storage service.
- **Rationale**: Satisfies constitution II and the stakeholder path pattern; the same string works as a blob name later.
- **Alternatives**: Storing file bytes in SQLite (rejected: inflates the database, breaks the "swap
  implementation" goal); storing under `wwwroot` (rejected: bypasses authorization).
- **Deviation noted**: the stakeholder document sketches `UploadAsync(Stream, fileName, contentType)`. Here the
  caller supplies the relative path instead of the file name, so a user-supplied name never reaches storage.

### D2. Upload sequence and failure handling
- **Decision**: validate -> authorize -> generate path -> save file -> save metadata. If saving metadata fails,
  delete the file just written (compensation). Replace-file follows the same order: write new file, update the
  record, then delete the old file.
- **Rationale**: FR-010 (no orphans or partial records) and the README guidance on unique paths.

### D3. Offline "malware" check
- **Decision**: `IFileContentValidator` performs a local check: (a) extension on the allow-list (`.pdf .doc .docx
  .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`), (b) leading bytes ("magic numbers") must match the extension
  (PDF `%PDF`, OOXML ZIP `PK`, legacy Office `D0 CF 11 E0`, JPEG `FF D8 FF`, PNG `89 50 4E 47`), (c) text files must
  contain no NUL bytes, (d) known dangerous signatures are rejected whatever the extension (Windows executable
  `MZ`, ELF, shebang `#!`).
- **Rationale**: Honest, testable and offline (clarification Q1). Documented as not a replacement for a real scanner.
- **Alternatives**: Windows Defender integration (environment dependent); simulated scanner (misleading).

### D4. Authorization model (single place)
- **Decision**: `DocumentService` exposes the only decision functions: *can view*, *can manage*, *can upload to
  project*. A document is **viewable** by: Administrator; its uploader; any user it is shared with (directly or
  via their department); members and the manager of its project; and a Team Lead for documents uploaded by users of
  the same department, **except** Personal Files without a project that were not shared with that Team Lead
  (clarification Q2). **Manageable** (edit/replace/delete) by: Administrator, the uploader, the Project Manager of
  the document's project, and a Team Lead for same-department uploads under the same exception. Sharing is
  allowed to those who can manage a document they own (uploader and Administrator).
- **Rationale**: Constitution III (service-level, IDOR-safe); queries for lists/search apply the same predicate in SQL
  so unauthorized rows never leave the database.
- **Team definition**: team = same `Department` (spec assumption).

### D5. Serving files
- **Decision**: Minimal API endpoints `GET /documents/{id}/download` and `GET /documents/{id}/preview`, both
  `RequireAuthorization()`, delegating to `DocumentService`. Preview is allowed only for PDF and images and is
  opened in a **new browser tab** with `Content-Disposition: inline`; download uses `attachment`. Unauthorized or
  unknown ids return the same `404` to avoid revealing existence.
- **Rationale**: The existing `X-Frame-Options: DENY` header would block iframe preview; a new tab needs no header
  change and keeps the headers unweakened (constitution technology constraints). `X-Content-Type-Options: nosniff`
  is already set globally.

### D6. Upload UI in Blazor Server
- **Decision**: `InputFile` with `@key` reset after success; read name/size/type into locals, copy the stream to a
  `MemoryStream` immediately with a 25 MB cap, then clear the file reference (stakeholder Blazor guidance);
  `multiple` selection supported; progress shown per file while copying; `Title` defaults to the file name without
  extension; one shared category/project/description/tags per batch (clarification Q3).
- **Rationale**: Avoids disposed-stream errors; 25 MB in memory is acceptable for the training scale.

### D7. Data model decisions
- **Decision**: integer keys; `Category` stored as text from a fixed list; tags in a child table
  `DocumentTag` for exact search; sharing in `DocumentShare` (target is a user **or** a department);
  activity in `DocumentActivity` **without a foreign key** to `Document` and with a title snapshot so records
  survive deletion (clarification Q4); duplicates allowed (clarification Q5).
- **Rationale**: Matches stakeholder constraints (integer id, text category, 255-char MIME type) and FR-027.

### D8. Search, sorting and paging
- **Decision**: EF Core queries with case-insensitive `Contains` on title, description, uploader name, project
  name and tag; authorization predicate first; ordering and filters in SQL; list pages are paged (50 per page).
  Indexes on uploader, project, task, category, upload date, share target and activity (data-model.md).
- **Rationale**: Meets the 2 s targets at the 500-document scale without extra infrastructure.

### D9. Notifications, dashboard, reports
- **Decision**: add `DocumentShared` and `DocumentAdded` to `NotificationType` (appended at the end) and reuse
  `INotificationService`. `DashboardService` gains the five most recent documents and a count.
  `DocumentReportService` aggregates by file type, uploader and access (downloads/previews) from
  `DocumentActivity`; the reports page is restricted to Administrators.

### D10. Login claim and database reset
- **Decision**: add the Department claim at login (additive; nothing existing changes). Developers must
  **delete the existing `ContosoDashboard.db*`** once, because `EnsureCreated()` will not add tables to an
  existing database (documented in quickstart).

## Known limitations (kept, per constitution V)

- The content check is not a real antivirus.
- Search covers metadata only, not file contents.
- Whole-file buffering in memory limits scalability beyond training use.
- No quotas, versioning or soft delete (out of scope).
- Concurrent edits of the same document: last write wins.
- Activity records are append-only but not tamper-proof against someone with direct database access.
