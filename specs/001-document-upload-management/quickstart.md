# Quickstart: Validating Document Upload and Management

A run-and-verify guide. It contains no implementation code; see [contracts/](contracts/) and
[data-model.md](data-model.md) for details.

## Prerequisites

- .NET 10 SDK; no other services required (offline).
- Test users (no password): Ni Kang (Employee), Floris Kregel (Team Lead), Camille Nicole (Project Manager),
  System Administrator.
- Sample files: a small PDF, a PNG, a `.txt`, a `.docx`, a file larger than 25 MB, an `.exe` renamed to `.pdf`, and a
  `.zip`.

## Setup

1. **Reset the database once** (new tables are not added to an existing database by `EnsureCreated()`):
   ```powershell
   cd ContosoDashboard
   Remove-Item ContosoDashboard.db*
   ```
2. Build and run:
   ```powershell
   dotnet restore
   dotnet build
   dotnet run
   ```
3. Open `http://localhost:5000/login` and sign in as the user named in each scenario.

## Validation scenarios

| # | Role | Steps | Expected |
|---|------|-------|----------|
| 1 | Employee (Ni Kang) | Documents -> Upload; choose PDF; title + category; confirm | Success message; row shows title, category, date, size, no project |
| 2 | Employee | Upload three files at once (PDF, PNG, txt) with one category and different titles | Three documents stored with their own titles and the shared category; one result per file |
| 3 | Employee | Upload the >25 MB file; then the `.zip`; then the renamed `.exe` | Each rejected with a clear message; nothing appears in the list or in `AppData/uploads` |
| 4 | Employee | Upload with empty title or no category | Blocked, missing fields indicated |
| 5 | Employee | Sort by size; filter by category and date range; search by a tag and by title | Lists reorder/filter correctly; results appear in <= 2 s |
| 6 | Employee | Preview the PDF and the PNG; download the txt | Preview opens in a new tab; download saves the file |
| 7 | Employee | Edit title/tags; replace the file; delete with confirmation | Changes visible; after delete the document and its file are gone |
| 8 | Employee A / Employee B | As A, note a document's id from the download link; as B (no rights) open `/documents/{id}/download` | `404`, no content (IDOR check) |
| 9 | Project Manager | Open a managed project; upload a document; as a member of that project view and download it | Visible to members; non-members cannot see or fetch it |
| 10 | Project Manager | Delete a member's document in the managed project; try the same in another project | First succeeds; second is refused |
| 11 | Team Lead | View a same-department member's documents; check a member's unshared Personal Files without project | Team documents visible; that Personal File is not |
| 12 | Employee | Share a document with another user and with a department | Recipient gets an in-app notification; document appears in "Shared with Me" |
| 13 | Employee | Open a task; upload a document from it | Listed on the task and in the task's project |
| 14 | Any user | Open the dashboard | "Recent Documents" shows the last 5; the summary shows the document count |
| 15 | Administrator | Open Document Reports; try the same URL as an Employee | Reports show types, uploaders, access; Employee is denied |
| 16 | Administrator | After actions above, review activity | Upload, download, preview, delete and share appear; entries remain after the document is deleted |
| 17 | Any | Stop the app, run `dotnet build` | 0 errors |

## Pass criteria

All scenarios behave as expected, no document is reachable without permission (scenarios 8-11), failed uploads leave
no files or records (scenario 3), and the timing targets in the spec hold for a normal local run.

## Cleaning up

Delete `ContosoDashboard.db*` and `AppData/uploads/` to return to a clean state.
