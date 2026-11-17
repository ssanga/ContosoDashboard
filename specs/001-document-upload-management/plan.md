# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` | **Date**: 2026-11-17 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add a document library to the existing ContosoDashboard Blazor Server application: upload (single and
multiple files) with metadata, browsing/search/sort/filter, preview and download, edit/replace/delete,
sharing with in-app notifications, project/task/dashboard integration, and an audit log with
administrator reports.

Technical approach (details in [research.md](research.md)): keep the single existing web project and its
layers (Models / Data / Services / Pages). Add four entities (`Document`, `DocumentTag`, `DocumentShare`,
`DocumentActivity`), a `DocumentService` that owns every authorization and validation rule, an
`IFileStorageService` abstraction with a `LocalFileStorageService` (files outside `wwwroot`, GUID-based
relative paths), a local `IFileContentValidator` for the offline malware/type check, and authorized
download/preview endpoints. No new NuGet packages are required.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (`net10.0`)

**Primary Dependencies**: ASP.NET Core Blazor Server and Razor Pages, Entity Framework Core 10 (SQLite provider); no new packages

**Storage**: SQLite (`ContosoDashboard.db`) for metadata; local filesystem under `AppData/uploads` (content root, outside `wwwroot`) for file content

**Testing**: No automated test project exists and the constitution does not require one; verification is by the scripted role-based scenarios in [quickstart.md](quickstart.md) plus `dotnet build`

**Target Platform**: Developer workstation (Windows/macOS/Linux) running offline

**Project Type**: Web application (single Blazor Server project)

**Performance Goals**: Upload of 25 MB in <= 30 s; document list <= 2 s for 500 documents; search <= 2 s; preview <= 3 s (spec SC-002..SC-005)

**Constraints**: Offline only, no cloud SDKs; 25 MB per-file limit; allow-listed file types; files never served from `wwwroot`; must not break existing pages or mock sign-in

**Scale/Scope**: Training deployment, a handful of users, up to ~500 documents per user; about 3 new pages, 3 reusable components, 3 new services, 4 new tables

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Gate | Pre-research | Post-design |
|-----------|------|--------------|-------------|
| I. Offline-First, Local-Only | No cloud SDK or network dependency; SQLite + local files | PASS | PASS - local storage, local content validator, no new packages |
| II. Infrastructure Abstraction | Storage reached only through an interface registered in DI | PASS | PASS - `IFileStorageService` / `LocalFileStorageService`; relative portable paths |
| III. Security by Default | Middleware + `[Authorize]` + service-level checks; IDOR protection; no user-supplied file names in paths; authorized file serving | PASS | PASS - all rules in `DocumentService`, endpoints require authorization and call the service, GUID paths |
| IV. Layered Architecture | Models/Data/Services/Pages; async; `.Include()`; indexes | PASS | PASS - see data model indexes and service contract |
| V. Simplicity & Documented Limitations | Simplest viable design; limitations documented | PASS | PASS - no repository layer, no background jobs, no new projects; limitations listed in research |
| Workflow gate | Builds and starts; flows verified for two roles + unauthorized attempt | n/a | Planned in quickstart |

No violations, so Complexity Tracking is empty.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md              # This file (/speckit-plan command output)
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   ├── service-interfaces.md
│   ├── http-endpoints.md
│   └── ui-contract.md
└── tasks.md             # Phase 2 output (/speckit-tasks command - NOT created by /speckit-plan)
```

### Source Code (repository root)

```text
ContosoDashboard/                         # existing single web project
├── Models/
│   ├── Document.cs                       # new: Document, DocumentTag, DocumentShare, DocumentActivity, DocumentCategory
│   └── Notification.cs                   # edit: add DocumentShared, DocumentAdded types
├── Data/
│   └── ApplicationDbContext.cs           # edit: DbSets, relationships, indexes
├── Services/
│   ├── IFileStorageService / LocalFileStorageService      # new (one file)
│   ├── IFileContentValidator / FileContentValidator       # new (one file)
│   ├── DocumentService.cs                # new: IDocumentService + implementation (rules, audit, notifications)
│   ├── DocumentReportService.cs          # new: admin reports (IDocumentReportService)
│   └── DashboardService.cs               # edit: recent documents + document count
├── Pages/
│   ├── Documents.razor                   # new: /documents (My Documents, Shared with Me, search, upload)
│   ├── DocumentReports.razor             # new: /documents/reports (Administrator only)
│   ├── Login.cshtml.cs                   # edit: add Department claim
│   ├── Index.razor                       # edit: Recent Documents widget + count card
│   ├── ProjectDetails.razor              # edit: project documents section
│   └── Tasks.razor                       # edit: per-task documents entry point
├── Shared/
│   ├── DocumentUploadModal.razor         # new
│   ├── DocumentList.razor                # new
│   ├── TaskDocuments.razor               # new
│   └── NavMenu.razor                     # edit: Documents (and Reports for Administrator)
├── Program.cs                            # edit: DI registrations, download/preview endpoints, upload folder
├── appsettings.json                      # edit: DocumentStorage settings
└── AppData/uploads/                      # runtime only, git-ignored

.gitignore                                # edit: ignore ContosoDashboard/AppData/
```

**Structure Decision**: Keep the existing single Blazor Server project and extend its current layers.
Services follow the existing "interface + implementation in one file" convention. No tests project is added
because nothing in the repository or constitution requires one; verification is scenario-based.

## Complexity Tracking

No constitution violations; nothing to justify.
