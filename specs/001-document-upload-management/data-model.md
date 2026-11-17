# Data Model: Document Upload and Management

Keys are integers, consistent with `User`, `Project`, `TaskItem`. Categories are stored as text.
The model is created by `EnsureCreated()`; see [quickstart.md](quickstart.md) for the one-time database reset.

## Entities

### Document
| Field | Type | Rules |
|-------|------|-------|
| DocumentId | int, PK | identity |
| Title | string(255) | required |
| Description | string(2000)? | optional |
| Category | string(50) | required; one of: Project Documents, Team Resources, Personal Files, Reports, Presentations, Other |
| OriginalFileName | string(255) | display only; never used to build a path |
| FilePath | string(500) | required, unique; relative `{uploaderId}/{projectId or personal}/{guid}.{ext}`; set before insert |
| FileType | string(255) | MIME type (long values for Office formats) |
| FileSize | long | bytes; 1..26,214,400 |
| UploadedDate | DateTime (UTC) | set automatically |
| UpdatedDate | DateTime? (UTC) | set on edit/replace |
| UploaderId | int, FK -> User | required |
| ProjectId | int?, FK -> Project | optional; uploader must be a member/manager (or admin) |
| TaskId | int?, FK -> TaskItem | optional; when set, ProjectId is set to the task's project |

Relationships: many `DocumentTag`, many `DocumentShare`. Deleting a Document deletes its tags and shares
(cascade) but **not** its activity records.

Indexes: `UploaderId`; `ProjectId`; `TaskId`; `Category`; `UploadedDate`; unique `FilePath`.

### DocumentTag
| Field | Type | Rules |
|-------|------|-------|
| DocumentTagId | int, PK | |
| DocumentId | int, FK -> Document | required, cascade |
| Tag | string(50) | required, stored trimmed and lower-cased; unique per document |

Index: `Tag`; unique (`DocumentId`, `Tag`).

### DocumentShare
| Field | Type | Rules |
|-------|------|-------|
| DocumentShareId | int, PK | |
| DocumentId | int, FK -> Document | required, cascade |
| SharedWithUserId | int?, FK -> User | exactly one of user/department must be set |
| SharedWithDepartment | string(100)? | whole team = department |
| SharedByUserId | int, FK -> User | the owner/manager performing the share |
| SharedDate | DateTime (UTC) | |

Indexes: `DocumentId`; `SharedWithUserId`; `SharedWithDepartment`.
Rule: sharing the same document with the same target twice is ignored (no duplicate rows).

### DocumentActivity
| Field | Type | Rules |
|-------|------|-------|
| DocumentActivityId | int, PK | |
| DocumentId | int | **no foreign key** so the record survives deletion |
| DocumentTitle | string(255) | snapshot at the time of the action |
| FileType | string(255) | snapshot, used for reports |
| ActionType | string(20) | Upload, Download, Preview, Edit, Replace, Delete, Share |
| UserId | int | acting user |
| ActivityDate | DateTime (UTC) | |
| Details | string(500)? | e.g. share target |

Indexes: `DocumentId`; `ActivityDate`; (`ActionType`, `ActivityDate`).
Append-only: the application exposes no update or delete operation for this table.

### Changes to existing entities
- `NotificationType`: append `DocumentShared`, `DocumentAdded` (existing numeric values unchanged).
- `User`, `Project`, `ProjectMember`, `TaskItem`: unchanged (navigation collections optional).
- Login claims: add `Department` (from `User.Department`).

## Validation rules (enforced in `DocumentService`)

- Extension in the allow-list and content matches it (see research D3); size <= 25 MB and > 0.
- Title and category required; category from the fixed list; at most 10 tags, each <= 50 characters.
- Project association only if the actor is a member or manager of that project, or an Administrator.

## State transitions

`Uploaded` -> (`Edited` | `Replaced` | `Shared`)* -> `Deleted` (permanent; file and metadata removed, activity kept).
There is no trash or version history (out of scope).

## Access matrix (summary; authoritative rules in research D4)

| Actor | View | Manage (edit/replace/delete) | Share |
|-------|------|------------------------------|-------|
| Uploader | own | own | own |
| Project member | project documents | no | no |
| Project Manager | their projects' documents | their projects' documents | their own uploads |
| Team Lead | same-department uploads (not unshared Personal Files without project) | same scope | their own uploads |
| Administrator | all | all | any |
| Share recipient (user or department) | shared documents | no | no |
