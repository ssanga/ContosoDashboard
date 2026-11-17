# Contract: User Interface

## Pages and access

| Route | Page | Access | Purpose |
|-------|------|--------|---------|
| `/documents` | Documents | `[Authorize]` (any role) | Tabs "My Documents" and "Shared with Me"; search box; sort and filter; upload button; per-row preview, download, edit, replace, share, delete |
| `/documents/reports` | Document Reports | `[Authorize(Policy = "Administrator")]` | Top file types, most active uploaders, access patterns, with a date range filter |
| `/projects/{id}` | Project Details (edited) | Existing (members only) | New "Documents" section; Project Manager can upload and delete |
| `/tasks` | Tasks (edited) | `[Authorize]` | Per-task "Documents" action opening the task documents panel (list, attach, upload) |
| `/` | Dashboard (edited) | `[Authorize]` | "Recent Documents" widget (last 5) and a document-count summary card |

Navigation: add "Documents" to the sidebar for all users and "Document Reports" for Administrators only.

## Components

- `DocumentUploadModal`: file picker (multiple), per-file title (default file name), shared category, optional project,
  description and tags; per-file progress and result; closes on full success; clears the file input with `@key`.
- `DocumentList`: table with title, category, upload date, size, project; sortable headers; category, project and
  date-range filters; paging; empty state ("No documents found").
- `TaskDocuments`: documents of one task, attach existing, upload from the task.

## Behaviors (testable)

- Upload: 3 clicks or fewer after selecting the file (open modal, fill required fields, confirm).
- Rejection messages: state the limit (25 MB) or list supported types; the failing file is named.
- Delete: requires a confirmation dialog inside the page (no browser `confirm`); cancel changes nothing.
- Preview: PDFs and images open in a new tab; other types show only Download.
- Search results and lists contain only documents the user may view; users without rights see no
  edit/replace/delete/share controls (controls are a convenience; the service enforces the rules).
- Success and error feedback uses the same alert style as existing pages.
- Accessibility basics: labelled inputs, keyboard-operable modal, text alternatives for icon-only buttons.
