using Microsoft.EntityFrameworkCore;
using ContosoDashboard.Data;
using ContosoDashboard.Models;

namespace ContosoDashboard.Services;

// ---------------------------------------------------------------------------
// Supporting types (see specs/001-document-upload-management/contracts/service-interfaces.md)
// ---------------------------------------------------------------------------

public class DocumentUploadRequest
{
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string Category { get; set; } = DocumentCategory.Other;
    public string? Description { get; set; }
    public int? ProjectId { get; set; }
    public int? TaskId { get; set; }
    public List<string> Tags { get; set; } = new();
}

public record DocumentUploadResult(bool Success, int? DocumentId, string? ErrorMessage);

public class DocumentMetadata
{
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Category { get; set; } = DocumentCategory.Other;
    public List<string> Tags { get; set; } = new();
}

public enum DocumentSortField
{
    UploadedDate,
    Title,
    Category,
    FileSize
}

public class DocumentQuery
{
    public string? Category { get; set; }
    public int? ProjectId { get; set; }
    public DateTime? From { get; set; }
    public DateTime? To { get; set; }
    public DocumentSortField SortBy { get; set; } = DocumentSortField.UploadedDate;
    public bool Descending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}

public class PagedResult<T>
{
    public List<T> Items { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public class DocumentSummary
{
    public int DocumentId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public DateTime UploadedDate { get; set; }
    public long FileSize { get; set; }
    public string FileType { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public int? ProjectId { get; set; }
    public string? ProjectName { get; set; }
    public int UploaderId { get; set; }
    public string UploaderName { get; set; } = string.Empty;
}

public class DocumentDetail : DocumentSummary
{
    public string? Description { get; set; }
    public int? TaskId { get; set; }
    public DateTime? UpdatedDate { get; set; }
    public List<string> Tags { get; set; } = new();
    public bool CanManage { get; set; }
}

public class DocumentFile
{
    public Stream Stream { get; set; } = Stream.Null;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
}

public interface IDocumentService
{
    // Upload & management
    Task<DocumentUploadResult> UploadAsync(int requestingUserId, DocumentUploadRequest request, Stream content);
    Task<bool> UpdateMetadataAsync(int requestingUserId, int documentId, DocumentMetadata metadata);
    Task<bool> ReplaceFileAsync(int requestingUserId, int documentId, string fileName, string contentType, Stream content);
    Task<bool> DeleteAsync(int requestingUserId, int documentId);

    // Reading
    Task<PagedResult<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery? query = null);
    Task<PagedResult<DocumentSummary>> GetProjectDocumentsAsync(int requestingUserId, int projectId, DocumentQuery? query = null);
    Task<PagedResult<DocumentSummary>> GetTaskDocumentsAsync(int requestingUserId, int taskId, DocumentQuery? query = null);
    Task<PagedResult<DocumentSummary>> GetSharedWithMeAsync(int requestingUserId, DocumentQuery? query = null);
    Task<PagedResult<DocumentSummary>> SearchAsync(int requestingUserId, string term, DocumentQuery? query = null);
    Task<DocumentDetail?> GetDocumentAsync(int requestingUserId, int documentId);

    // File access (used by the HTTP endpoints; records the activity)
    Task<DocumentFile?> OpenForDownloadAsync(int requestingUserId, int documentId);
    Task<DocumentFile?> OpenForPreviewAsync(int requestingUserId, int documentId);

    // Sharing & integration
    Task<bool> ShareWithUserAsync(int requestingUserId, int documentId, int targetUserId);
    Task<bool> ShareWithDepartmentAsync(int requestingUserId, int documentId, string department);
    Task<bool> AttachToTaskAsync(int requestingUserId, int documentId, int taskId);

    // Dashboard
    Task<List<DocumentSummary>> GetRecentDocumentsAsync(int requestingUserId, int count = 5);
    Task<int> GetDocumentCountAsync(int requestingUserId);
}

// ---------------------------------------------------------------------------
// Implementation
// ---------------------------------------------------------------------------

public class DocumentService : IDocumentService
{
    private const int MaxTags = 10;
    private const int MaxTagLength = 50;

    private static readonly Dictionary<string, string> MimeByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        [".txt"] = "text/plain",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".png"] = "image/png"
    };

    private readonly ApplicationDbContext _context;
    private readonly IFileStorageService _storage;
    private readonly IFileContentValidator _validator;
    private readonly INotificationService _notificationService;

    public DocumentService(
        ApplicationDbContext context,
        IFileStorageService storage,
        IFileContentValidator validator,
        INotificationService notificationService)
    {
        _context = context;
        _storage = storage;
        _validator = validator;
        _notificationService = notificationService;
    }

    // ----- Shared authorization helpers (research.md D4) ---------------------------------

    /// <summary>Team-level roles (hierarchical: ProjectManager includes TeamLead permissions).</summary>
    private static bool HasTeamLevelRole(User user) =>
        user.Role == UserRole.TeamLead || user.Role == UserRole.ProjectManager;

    /// <summary>
    /// Documents the user may VIEW: Administrator (all); uploader; share recipients (user or
    /// department); project members and manager; team-level roles for same-department uploads,
    /// except Personal Files without a project (visible to them only if shared).
    /// </summary>
    private IQueryable<Document> ViewableDocuments(User user)
    {
        var query = _context.Documents.AsNoTracking();
        if (user.Role == UserRole.Administrator)
            return query;

        var userId = user.UserId;
        var department = user.Department;
        var teamLevel = HasTeamLevelRole(user);

        return query.Where(d =>
            d.UploaderId == userId
            || d.Shares.Any(s => s.SharedWithUserId == userId
                                 || (department != null && s.SharedWithDepartment == department))
            || (d.ProjectId != null &&
                (d.Project!.ProjectManagerId == userId
                 || d.Project.ProjectMembers.Any(m => m.UserId == userId)))
            || (teamLevel && department != null && d.Uploader.Department == department
                && !(d.Category == DocumentCategory.PersonalFiles && d.ProjectId == null)));
    }

    /// <summary>Whether the user may MANAGE (edit/replace/delete) the given document.</summary>
    protected bool CanManage(User user, Document document, string? uploaderDepartment, int? projectManagerId)
    {
        if (user.Role == UserRole.Administrator) return true;
        if (document.UploaderId == user.UserId) return true;
        if (document.ProjectId != null && projectManagerId == user.UserId) return true;

        return HasTeamLevelRole(user)
               && user.Department != null
               && uploaderDepartment == user.Department
               && !(document.Category == DocumentCategory.PersonalFiles && document.ProjectId == null);
    }

    /// <summary>Whether the user may attach a document to the project (member, manager or Administrator).</summary>
    private async Task<bool> CanUploadToProjectAsync(User user, int projectId)
    {
        if (user.Role == UserRole.Administrator)
            return await _context.Projects.AnyAsync(p => p.ProjectId == projectId);

        return await _context.Projects.AnyAsync(p =>
            p.ProjectId == projectId
            && (p.ProjectManagerId == user.UserId || p.ProjectMembers.Any(m => m.UserId == user.UserId)));
    }

    /// <summary>Appends an audit record (append-only; no update/delete operation exists).</summary>
    private void AddActivity(string actionType, int documentId, string title, string fileType, int userId, string? details = null)
    {
        _context.DocumentActivities.Add(new DocumentActivity
        {
            DocumentId = documentId,
            DocumentTitle = Truncate(title, 255),
            FileType = Truncate(fileType, 255),
            ActionType = actionType,
            UserId = userId,
            ActivityDate = DateTime.UtcNow,
            Details = details == null ? null : Truncate(details, 500)
        });
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];

    private static List<string> NormalizeTags(IEnumerable<string>? tags) =>
        (tags ?? Enumerable.Empty<string>())
            .Select(t => t.Trim().ToLowerInvariant())
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

    private static string SanitizeFileName(string fileName)
    {
        var name = Path.GetFileName(fileName ?? string.Empty);
        var invalid = Path.GetInvalidFileNameChars();
        name = new string(name.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray());
        if (string.IsNullOrWhiteSpace(name)) name = "document";
        return Truncate(name, 255);
    }

    private static DocumentUploadResult Fail(string message) => new(false, null, message);

    // ----- Upload (US1) -------------------------------------------------------------------

    public async Task<DocumentUploadResult> UploadAsync(int requestingUserId, DocumentUploadRequest request, Stream content)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == requestingUserId);
        if (user == null)
            return Fail("User not found.");

        // 1. Validate
        var title = (request.Title ?? string.Empty).Trim();
        if (title.Length == 0)
            return Fail("Title is required.");
        if (title.Length > 255)
            return Fail("Title must be 255 characters or fewer.");

        if (string.IsNullOrWhiteSpace(request.Category) || !DocumentCategory.All.Contains(request.Category))
            return Fail("Please select a valid category.");

        var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        if (description != null && description.Length > 2000)
            return Fail("Description must be 2000 characters or fewer.");

        var tags = NormalizeTags(request.Tags);
        if (tags.Count > MaxTags)
            return Fail($"A document can have at most {MaxTags} tags.");
        if (tags.Any(t => t.Length > MaxTagLength))
            return Fail($"Each tag must be {MaxTagLength} characters or fewer.");

        if (!content.CanSeek)
            return Fail("The file could not be read.");

        var validation = await _validator.ValidateAsync(request.FileName, request.ContentType, content, content.Length);
        if (!validation.IsValid)
            return Fail(validation.ErrorMessage ?? "The file was rejected.");

        // 2. Authorize
        var projectId = request.ProjectId;
        if (request.TaskId.HasValue)
        {
            var task = await _context.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.TaskId == request.TaskId.Value);
            if (task == null)
                return Fail("Task not found.");
            if (task.ProjectId.HasValue)
                projectId = task.ProjectId; // documents attached to a task belong to the task's project
        }

        if (projectId.HasValue && !await CanUploadToProjectAsync(user, projectId.Value))
            return Fail("You are not allowed to add documents to this project.");

        // 3. Generate the unique relative path BEFORE any insert
        var originalName = SanitizeFileName(request.FileName);
        var extension = Path.GetExtension(originalName).ToLowerInvariant();
        var relativePath = $"{user.UserId}/{(projectId.HasValue ? projectId.Value.ToString() : "personal")}/{Guid.NewGuid():N}{extension}";
        var mimeType = MimeByExtension.TryGetValue(extension, out var mime)
            ? mime
            : (string.IsNullOrWhiteSpace(request.ContentType) ? "application/octet-stream" : request.ContentType);

        var length = content.Length;
        content.Position = 0;

        // 4. Save the file, then 5. save the metadata (compensate by deleting the file on failure)
        try
        {
            await _storage.UploadAsync(content, relativePath, mimeType);
        }
        catch (Exception)
        {
            return Fail("The file could not be saved. Please try again.");
        }

        try
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            var document = new Document
            {
                Title = title,
                Description = description,
                Category = request.Category,
                OriginalFileName = originalName,
                FilePath = relativePath,
                FileType = Truncate(mimeType, 255),
                FileSize = length,
                UploadedDate = DateTime.UtcNow,
                UploaderId = user.UserId,
                ProjectId = projectId,
                TaskId = request.TaskId,
                Tags = tags.Select(t => new DocumentTag { Tag = t }).ToList()
            };

            _context.Documents.Add(document);
            await _context.SaveChangesAsync();

            AddActivity(DocumentActionType.Upload, document.DocumentId, document.Title, document.FileType, user.UserId);
            await _context.SaveChangesAsync();

            await NotifyProjectMembersAsync(document, user);

            await transaction.CommitAsync();
            return new DocumentUploadResult(true, document.DocumentId, null);
        }
        catch (Exception)
        {
            // No orphaned file: remove what was just written
            try { await _storage.DeleteAsync(relativePath); } catch { /* best effort */ }
            _context.ChangeTracker.Clear();
            return Fail("The document could not be saved. Please try again.");
        }
    }

    private async Task NotifyProjectMembersAsync(Document document, User uploader)
    {
        if (!document.ProjectId.HasValue) return;

        var project = await _context.Projects
            .AsNoTracking()
            .Include(p => p.ProjectMembers)
            .FirstOrDefaultAsync(p => p.ProjectId == document.ProjectId.Value);
        if (project == null) return;

        var recipients = project.ProjectMembers.Select(m => m.UserId)
            .Append(project.ProjectManagerId)
            .Where(id => id != uploader.UserId)
            .Distinct();

        foreach (var recipientId in recipients)
        {
            await _notificationService.CreateNotificationAsync(new Notification
            {
                UserId = recipientId,
                Title = "New document added",
                Message = Truncate($"{uploader.DisplayName} added \"{document.Title}\" to project {project.Name}.", 1000),
                Type = NotificationType.DocumentAdded,
                Priority = NotificationPriority.Informational
            });
        }
    }

    // ----- Reading (US1 basic) ------------------------------------------------------------

    public async Task<PagedResult<DocumentSummary>> GetMyDocumentsAsync(int requestingUserId, DocumentQuery? query = null)
    {
        query ??= new DocumentQuery();
        var page = Math.Max(1, query.Page);
        var pageSize = query.PageSize <= 0 ? 50 : Math.Min(query.PageSize, 200);

        var baseQuery = _context.Documents
            .AsNoTracking()
            .Where(d => d.UploaderId == requestingUserId);

        var total = await baseQuery.CountAsync();

        var documents = await baseQuery
            .Include(d => d.Project)
            .Include(d => d.Uploader)
            .OrderByDescending(d => d.UploadedDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return new PagedResult<DocumentSummary>
        {
            Items = documents.Select(ToSummary).ToList(),
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    private static DocumentSummary ToSummary(Document d) => new()
    {
        DocumentId = d.DocumentId,
        Title = d.Title,
        Category = d.Category,
        UploadedDate = d.UploadedDate,
        FileSize = d.FileSize,
        FileType = d.FileType,
        OriginalFileName = d.OriginalFileName,
        ProjectId = d.ProjectId,
        ProjectName = d.Project?.Name,
        UploaderId = d.UploaderId,
        UploaderName = d.Uploader?.DisplayName ?? string.Empty
    };

    // ----- Not yet implemented (later user stories) ----------------------------------------

    public Task<bool> UpdateMetadataAsync(int requestingUserId, int documentId, DocumentMetadata metadata) =>
        throw new NotImplementedException("Planned for user story 4 (T024).");

    public Task<bool> ReplaceFileAsync(int requestingUserId, int documentId, string fileName, string contentType, Stream content) =>
        throw new NotImplementedException("Planned for user story 4 (T024).");

    public Task<bool> DeleteAsync(int requestingUserId, int documentId) =>
        throw new NotImplementedException("Planned for user story 4 (T024).");

    public Task<PagedResult<DocumentSummary>> GetProjectDocumentsAsync(int requestingUserId, int projectId, DocumentQuery? query = null) =>
        throw new NotImplementedException("Planned for user story 3 (T022).");

    public Task<PagedResult<DocumentSummary>> GetTaskDocumentsAsync(int requestingUserId, int taskId, DocumentQuery? query = null) =>
        throw new NotImplementedException("Planned for user story 6 (T030).");

    public Task<PagedResult<DocumentSummary>> GetSharedWithMeAsync(int requestingUserId, DocumentQuery? query = null) =>
        throw new NotImplementedException("Planned for user story 5 (T027).");

    public Task<PagedResult<DocumentSummary>> SearchAsync(int requestingUserId, string term, DocumentQuery? query = null) =>
        throw new NotImplementedException("Planned for user story 2 (T018).");

    public Task<DocumentDetail?> GetDocumentAsync(int requestingUserId, int documentId) =>
        throw new NotImplementedException("Planned for user story 2 (T018).");

    public Task<DocumentFile?> OpenForDownloadAsync(int requestingUserId, int documentId) =>
        throw new NotImplementedException("Planned for user story 2 (T018).");

    public Task<DocumentFile?> OpenForPreviewAsync(int requestingUserId, int documentId) =>
        throw new NotImplementedException("Planned for user story 2 (T018).");

    public Task<bool> ShareWithUserAsync(int requestingUserId, int documentId, int targetUserId) =>
        throw new NotImplementedException("Planned for user story 5 (T027).");

    public Task<bool> ShareWithDepartmentAsync(int requestingUserId, int documentId, string department) =>
        throw new NotImplementedException("Planned for user story 5 (T027).");

    public Task<bool> AttachToTaskAsync(int requestingUserId, int documentId, int taskId) =>
        throw new NotImplementedException("Planned for user story 6 (T030).");

    public Task<List<DocumentSummary>> GetRecentDocumentsAsync(int requestingUserId, int count = 5) =>
        throw new NotImplementedException("Planned for user story 6 (T030).");

    public Task<int> GetDocumentCountAsync(int requestingUserId) =>
        throw new NotImplementedException("Planned for user story 6 (T030).");
}
