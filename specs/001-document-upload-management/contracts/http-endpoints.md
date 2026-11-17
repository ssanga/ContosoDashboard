# Contract: HTTP Endpoints

Files are never reachable by a static URL. Both endpoints require an authenticated session and delegate every
decision to `IDocumentService`.

## GET /documents/{id}/download

| Aspect | Contract |
|--------|----------|
| Auth | Cookie authentication required; anonymous -> redirect to `/login` |
| Success | `200` with the file; `Content-Disposition: attachment; filename="<original file name, sanitized>"`; `Content-Type` = stored MIME type |
| Not found or not permitted | `404` with no body (same response for both, so existence is not revealed) |
| Side effect | Records a `Download` activity |

## GET /documents/{id}/preview

| Aspect | Contract |
|--------|----------|
| Auth | Same as download |
| Success | `200`, `Content-Disposition: inline`, only for PDF, JPEG and PNG; opened in a new browser tab |
| Other file types | `404` (use download instead) |
| Not found or not permitted | `404` with no body |
| Side effect | Records a `Preview` activity |

## Common rules

- The `id` is an integer; any non-integer value returns `404`.
- Response headers keep the application's existing security headers; `X-Content-Type-Options: nosniff` applies.
- File names in headers come from the stored original name after removing path separators and control
  characters; they are never used to locate the file (the stored GUID path is).
- Uploads do not use an HTTP endpoint: they run through the Blazor component and `IDocumentService.UploadAsync`.
