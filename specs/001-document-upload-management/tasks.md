---

description: "Task list for Document Upload and Management"
---

# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Not requested in the specification and the constitution does not require them; no test project exists.
Each story is verified with the scenarios in [quickstart.md](quickstart.md) (listed per story below).

**Organization**: Tasks are grouped by user story so each story can be implemented and verified independently.
All paths are relative to the repository root; application code lives in `ContosoDashboard/` (the web project).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an incomplete task)
- **[Story]**: User story the task belongs to (US1..US7)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Configuration and housekeeping needed before any code

- [X] T001 Add `ContosoDashboard/AppData/` to `.gitignore` so uploaded files are never committed
- [X] T002 [P] Add a `DocumentStorage` section to `ContosoDashboard/appsettings.json` with `RootPath` = `AppData/uploads` and `MaxFileSizeBytes` = `26214400`
- [X] T003 [P] Add a claim of type `"Department"` (value from `user.Department`) to the claims list in `ContosoDashboard/Pages/Login.cshtml.cs` next to the existing NameIdentifier/Name/Email/Role claims

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Entities, storage, validation, authorization helpers and wiring shared by every story

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T004 Create `ContosoDashboard/Models/Document.cs` with entities `Document`, `DocumentTag`, `DocumentShare`, `DocumentActivity` and a static `DocumentCategory` list. Constraints verbatim from data-model.md:
  - Document: `DocumentId` int PK; `Title` string(255) required; `Description` string(2000)? optional; `Category` string(50) required, one of "Project Documents", "Team Resources", "Personal Files", "Reports", "Presentations", "Other"; `OriginalFileName` string(255); `FilePath` string(500) required, unique, relative `{uploaderId}/{projectId or personal}/{guid}.{ext}`; `FileType` string(255); `FileSize` long (1..26,214,400); `UploadedDate` DateTime UTC; `UpdatedDate` DateTime? UTC; `UploaderId` int FK -> User required; `ProjectId` int? FK -> Project; `TaskId` int? FK -> TaskItem
  - DocumentTag: `DocumentTagId` PK; `DocumentId` FK cascade; `Tag` string(50) required, trimmed and lower-cased, unique per document
  - DocumentShare: `DocumentShareId` PK; `DocumentId` FK cascade; `SharedWithUserId` int? FK -> User; `SharedWithDepartment` string(100)?; exactly one of user/department set; `SharedByUserId` int FK -> User; `SharedDate` DateTime UTC
  - DocumentActivity: `DocumentActivityId` PK; `DocumentId` int with **no foreign key**; `DocumentTitle` string(255) snapshot; `FileType` string(255) snapshot; `ActionType` string(20) one of Upload, Download, Preview, Edit, Replace, Delete, Share; `UserId` int; `ActivityDate` DateTime UTC; `Details` string(500)?
- [X] T005 [P] Append `DocumentShared` and `DocumentAdded` at the **end** of the `NotificationType` enum in `ContosoDashboard/Models/Notification.cs` (existing values unchanged)
- [X] T006 Register the four new `DbSet`s and configure relationships, cascades (Document -> tags and shares only; never activity) and indexes in `ContosoDashboard/Data/ApplicationDbContext.cs`: Document `UploaderId`, `ProjectId`, `TaskId`, `Category`, `UploadedDate`, unique `FilePath`; DocumentTag `Tag` and unique (`DocumentId`, `Tag`); DocumentShare `DocumentId`, `SharedWithUserId`, `SharedWithDepartment`; DocumentActivity `DocumentId`, `ActivityDate`, (`ActionType`, `ActivityDate`)
- [X] T007 [P] Create `ContosoDashboard/Services/FileStorageService.cs` containing `IFileStorageService` (`UploadAsync(Stream, string relativePath, string contentType)`, `DownloadAsync`, `DeleteAsync`, `GetUrlAsync`) and `LocalFileStorageService` reading `DocumentStorage:RootPath` (resolved against the content root, outside `wwwroot`), rejecting rooted paths and `..`, creating folders as needed (contracts/service-interfaces.md)
- [X] T008 [P] Create `ContosoDashboard/Services/FileContentValidator.cs` containing `IFileContentValidator`, `FileValidationResult` and `FileContentValidator`: allow-list `.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png`; size > 0 and <= 26,214,400 bytes; magic numbers must match the extension (PDF `%PDF`, OOXML `PK`, legacy Office `D0 CF 11 E0`, JPEG `FF D8 FF`, PNG `89 50 4E 47`); text files must contain no NUL bytes; reject executables (`MZ`, ELF) and shebang (`#!`) regardless of extension; clear error messages stating the 25 MB limit or the supported types
- [X] T009 Create `ContosoDashboard/Services/DocumentService.cs` with `IDocumentService` exactly as in contracts/service-interfaces.md, the supporting types (`DocumentUploadRequest`, `DocumentUploadResult`, `DocumentQuery`, `DocumentSummary`, `DocumentDetail`, `DocumentFile`, `PagedResult<T>`), the class skeleton with injected `ApplicationDbContext`, `IFileStorageService`, `IFileContentValidator`, `INotificationService`, and the shared private helpers: a `CanView`/`CanManage` authorization predicate usable inside EF queries (rules in research.md D4, including the Personal Files exception and team = same `Department`) and `RecordActivityAsync` (append-only, snapshots title and type). Methods not yet implemented throw `NotImplementedException` until their story
- [X] T010 Register `IFileStorageService` -> `LocalFileStorageService`, `IFileContentValidator` -> `FileContentValidator` and `IDocumentService` -> `DocumentService` (scoped) in `ContosoDashboard/Program.cs`
- [X] T011 [P] Add a "Documents" `NavLink` (href `documents`, Bootstrap icon) to `ContosoDashboard/Shared/NavMenu.razor`
- [X] T012 Delete any existing `ContosoDashboard/ContosoDashboard.db*` (new tables are not added by `EnsureCreated()`), run `dotnet build` (0 errors) and `dotnet run` once to confirm the new schema is created

**Checkpoint**: Foundation ready - user stories can begin

---

## Phase 3: User Story 1 - Upload a document with its details (Priority: P1) MVP

**Goal**: A user uploads one or many documents with title, category and optional details and sees confirmation.

**Independent Test**: Quickstart scenarios 1-4: upload valid files, see them recorded with date, uploader, size and type; invalid files are rejected with clear messages and store nothing.

### Implementation for User Story 1

- [X] T013 [US1] Implement `UploadAsync` in `ContosoDashboard/Services/DocumentService.cs`: validate (title and category required, category in list, at most 10 tags each <= 50 characters, content check via `IFileContentValidator`) -> authorize (project association only for members/manager/Administrator) -> generate `{uploaderId}/{projectId or personal}/{guid}.{ext}` **before** insert -> save file -> save metadata (UTC date, uploader, size, MIME type up to 255 characters) -> on database failure delete the saved file; record `Upload` activity; notify other project members with `DocumentAdded` when a project is set; normalise tags (trimmed, lower-case, unique)
- [X] T014 [P] [US1] Create `ContosoDashboard/Shared/DocumentUploadModal.razor`: multiple `InputFile` with `@key` reset after success, copy each file to a `MemoryStream` immediately (cap 25 MB) and clear the file reference, per-file title (default file name without extension), one shared category/project/description/tags per batch, per-file progress and result messages, project dropdown limited to the user's projects (research.md D6, ui-contract.md)
- [X] T015 [P] [US1] Create `ContosoDashboard/Shared/DocumentList.razor` (basic): table with title, category, upload date, size, project; empty state "No documents found"
- [X] T016 [US1] Implement basic `GetMyDocumentsAsync` (own uploads, newest first, paged 50) in `ContosoDashboard/Services/DocumentService.cs` using `.Include()` for project and uploader
- [X] T017 [US1] Create `ContosoDashboard/Pages/Documents.razor` (`@page "/documents"`, `[Authorize]`) with an Upload button opening `DocumentUploadModal`, the "My Documents" list, and success/error alerts

**Checkpoint**: Upload and basic listing work end to end

---

## Phase 4: User Story 2 - Find and open my documents (Priority: P1)

**Goal**: Sort, filter, search, preview and download documents the user may access.

**Independent Test**: Quickstart scenarios 5, 6 and 8: sort/filter/search results are correct and fast; preview and download work; another user cannot fetch the document.

### Implementation for User Story 2

- [ ] T018 [US2] In `ContosoDashboard/Services/DocumentService.cs` extend `GetMyDocumentsAsync` with `DocumentQuery` sorting (title, upload date, category, file size) and filters (category, project, date range); implement `SearchAsync` (case-insensitive match on title, description, tags, uploader name, project name, with the view-authorization predicate applied first), `GetDocumentAsync`, `OpenForDownloadAsync` and `OpenForPreviewAsync` (preview only for PDF/JPEG/PNG; each records `Download`/`Preview` activity)
- [ ] T019 [US2] Add `GET /documents/{id:int}/download` and `GET /documents/{id:int}/preview` endpoints in `ContosoDashboard/Program.cs` per contracts/http-endpoints.md: `RequireAuthorization()`, resolve the current user id from the claims, delegate to `IDocumentService`, return `404` with no body when missing or not permitted, `attachment` vs `inline` disposition, sanitized file name
- [ ] T020 [US2] Extend `ContosoDashboard/Shared/DocumentList.razor`: sortable column headers, category/project/date-range filters, paging, and Preview (new tab, PDF/images only) and Download links to the endpoints
- [ ] T021 [US2] Update `ContosoDashboard/Pages/Documents.razor` with a search box and filter/sort wiring

**Checkpoint**: Users can find, preview and download documents; unauthorized access is denied

---

## Phase 5: User Story 3 - Project documents with role-based access (Priority: P2)

**Goal**: Project documents visible to members; Project Managers, Team Leads and Administrators have their scoped rights.

**Independent Test**: Quickstart scenarios 9-11.

### Implementation for User Story 3

- [ ] T022 [US3] Implement `GetProjectDocumentsAsync` in `ContosoDashboard/Services/DocumentService.cs` (members and manager of the project, Administrator) and verify in the shared predicates that Team Leads see same-department uploads except Personal Files without project that were not shared with them, and that Project Managers can upload to and manage documents of their own projects only
- [ ] T023 [US3] Add a "Documents" section to `ContosoDashboard/Pages/ProjectDetails.razor` using `DocumentList` and `DocumentUploadModal` pre-filled with the project; upload offered to members and the manager

**Checkpoint**: Project documents and role scopes behave per spec

---

## Phase 6: User Story 4 - Edit, replace and delete my documents (Priority: P2)

**Goal**: Maintain documents after upload.

**Independent Test**: Quickstart scenarios 7 and 10.

### Implementation for User Story 4

- [ ] T024 [US4] Implement `UpdateMetadataAsync` (title, description, category, tags), `ReplaceFileAsync` (validate, write new file, update record, then delete old file; type and size refreshed) and `DeleteAsync` (permanent removal of file, metadata, tags and shares; activity kept) in `ContosoDashboard/Services/DocumentService.cs`, all gated by the manage predicate and recording `Edit`/`Replace`/`Delete` activity
- [ ] T025 [P] [US4] Create `ContosoDashboard/Shared/DocumentEditModal.razor` for metadata edit and file replacement (same `@key` and `MemoryStream` pattern as the upload modal)
- [ ] T026 [US4] Wire Edit, Replace and Delete actions into `ContosoDashboard/Shared/DocumentList.razor` and `ContosoDashboard/Pages/Documents.razor`: controls shown only when permitted, in-page confirmation dialog before delete (no browser `confirm`), refresh after each action

**Checkpoint**: Full document lifecycle works

---

## Phase 7: User Story 5 - Share documents and get notified (Priority: P3)

**Goal**: Share with users or departments; recipients notified and see "Shared with Me".

**Independent Test**: Quickstart scenario 12.

### Implementation for User Story 5

- [ ] T027 [US5] Implement `ShareWithUserAsync`, `ShareWithDepartmentAsync` (ignore duplicate targets, record `Share` activity, create `DocumentShared` notifications for the user or each member of the department) and `GetSharedWithMeAsync` in `ContosoDashboard/Services/DocumentService.cs`
- [ ] T028 [P] [US5] Create `ContosoDashboard/Shared/DocumentShareModal.razor` to pick a user or a department (use the existing `IUserService`; add a list-users/departments method there only if none exists)
- [ ] T029 [US5] Add the "Shared with Me" tab and the Share action to `ContosoDashboard/Pages/Documents.razor`

**Checkpoint**: Sharing and notifications work

---

## Phase 8: User Story 6 - Documents in tasks and on the dashboard (Priority: P3)

**Goal**: Task integration and dashboard widgets.

**Independent Test**: Quickstart scenarios 13 and 14.

### Implementation for User Story 6

- [ ] T030 [US6] Implement `AttachToTaskAsync` (sets the document's project to the task's project), `GetTaskDocumentsAsync`, `GetRecentDocumentsAsync` (default 5) and `GetDocumentCountAsync` in `ContosoDashboard/Services/DocumentService.cs`, applying the view predicate
- [ ] T031 [P] [US6] Create `ContosoDashboard/Shared/TaskDocuments.razor` (list, attach existing, upload via `DocumentUploadModal` with `TaskId`)
- [ ] T032 [US6] Add a per-task "Documents" action to `ContosoDashboard/Pages/Tasks.razor` opening `TaskDocuments`
- [ ] T033 [US6] Extend `IDashboardService`/`DashboardService` in `ContosoDashboard/Services/DashboardService.cs` (and its data type) with recent documents and document count
- [ ] T034 [US6] Add the "Recent Documents" widget and a document-count summary card to `ContosoDashboard/Pages/Index.razor`

**Checkpoint**: Documents appear where users already work

---

## Phase 9: User Story 7 - Activity audit and reports (Priority: P3)

**Goal**: Administrators see reports built from the append-only activity record.

**Independent Test**: Quickstart scenarios 15 and 16.

### Implementation for User Story 7

- [ ] T035 [US7] Create `ContosoDashboard/Services/DocumentReportService.cs` with `IDocumentReportService` (top document types, most active uploaders, access patterns from `DocumentActivity`, optional date range); every method re-checks that the requesting user is an Administrator and returns empty results otherwise
- [ ] T036 [US7] Create `ContosoDashboard/Pages/DocumentReports.razor` (`@page "/documents/reports"`, `[Authorize(Policy = "Administrator")]`) showing the three reports with a date-range filter
- [ ] T037 [US7] Register `IDocumentReportService` in `ContosoDashboard/Program.cs` and add a "Document Reports" link visible only to Administrators in `ContosoDashboard/Shared/NavMenu.razor`

**Checkpoint**: All user stories complete

---

## Phase 10: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Update `README.md`: add Document Management to features, pages table and project structure, storage location and the offline content-check limitation
- [ ] T039 [P] Replace the LocalDB reset instructions in `StakeholderDocs/document-upload-and-management-feature.md` (Database Setup Requirements) with the SQLite equivalent (delete `ContosoDashboard.db*`)
- [ ] T040 Run every scenario in `specs/001-document-upload-management/quickstart.md` (roles: Employee, Team Lead, Project Manager, Administrator; include the unauthorized-access attempts) and fix defects found
- [ ] T041 Run `dotnet restore`, `dotnet build` (0 errors) and `dotnet run`; confirm existing pages (dashboard, tasks, projects, team, notifications, profile) still work and no uploaded file is reachable under `wwwroot`

---

## Dependencies & Execution Order

- **Setup (Phase 1)** -> **Foundational (Phase 2)** blocks everything.
- **US1 (P1)** starts after Phase 2 and is the MVP. **US2 (P1)** depends on US1's list and page.
- **US3** depends on US1/US2 (reuses list, upload modal and predicates). **US4** depends on US1/US2.
- **US5** depends on US2 (list and download); **US6** depends on US1 (upload modal) and on T033 for the dashboard; **US7** depends only on the activity records written by earlier stories.
- **Polish** after all desired stories.
- Same-file tasks are sequential: `DocumentService.cs` (T009, T013, T016, T018, T022, T024, T027, T030), `Documents.razor` (T017, T021, T026, T029), `DocumentList.razor` (T015, T020, T026), `Program.cs` (T010, T019, T037), `NavMenu.razor` (T011, T037).

### Parallel opportunities

- Phase 1: T002, T003 together.
- Phase 2: T005, T007, T008, T011 together (T004 -> T006 sequential; T009 after T004, T007, T008).
- US1: T014 and T015 together while T013/T016 are written.
- US4: T025; US5: T028; US6: T031 can each be built in parallel with their service task.
- Polish: T038 and T039 together.

## Implementation Strategy

1. **MVP**: Phase 1, Phase 2, then US1 (T001-T017). Validate quickstart scenarios 1-4.
2. Add **US2** (T018-T021): the library becomes usable (find, preview, download). This completes both P1 stories.
3. Add **US3** and **US4** (P2), then **US5**, **US6**, **US7** (P3), validating each story's scenarios before moving on.
4. Finish with Polish (T038-T041). Commit after each completed story.
