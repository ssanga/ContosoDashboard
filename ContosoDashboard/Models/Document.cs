using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

/// <summary>Allowed document categories (stored as text).</summary>
public static class DocumentCategory
{
    public const string ProjectDocuments = "Project Documents";
    public const string TeamResources = "Team Resources";
    public const string PersonalFiles = "Personal Files";
    public const string Reports = "Reports";
    public const string Presentations = "Presentations";
    public const string Other = "Other";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other
    };
}

/// <summary>Allowed activity action names for <see cref="DocumentActivity.ActionType"/>.</summary>
public static class DocumentActionType
{
    public const string Upload = "Upload";
    public const string Download = "Download";
    public const string Preview = "Preview";
    public const string Edit = "Edit";
    public const string Replace = "Replace";
    public const string Delete = "Delete";
    public const string Share = "Share";
}

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    /// <summary>One of <see cref="DocumentCategory.All"/>.</summary>
    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = DocumentCategory.Other;

    /// <summary>Display only; never used to build a storage path.</summary>
    [Required]
    [MaxLength(255)]
    public string OriginalFileName { get; set; } = string.Empty;

    /// <summary>Relative, portable path: {uploaderId}/{projectId or personal}/{guid}.{ext}. Unique.</summary>
    [Required]
    [MaxLength(500)]
    public string FilePath { get; set; } = string.Empty;

    /// <summary>MIME type (long values for Office formats).</summary>
    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    /// <summary>Size in bytes (1..26,214,400).</summary>
    public long FileSize { get; set; }

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedDate { get; set; }

    [Required]
    public int UploaderId { get; set; }

    public int? ProjectId { get; set; }

    public int? TaskId { get; set; }

    // Navigation properties
    [ForeignKey("UploaderId")]
    public virtual User Uploader { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    [ForeignKey("TaskId")]
    public virtual TaskItem? RelatedTask { get; set; }

    public virtual ICollection<DocumentTag> Tags { get; set; } = new List<DocumentTag>();
    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
}

public class DocumentTag
{
    [Key]
    public int DocumentTagId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    /// <summary>Stored trimmed and lower-cased; unique per document.</summary>
    [Required]
    [MaxLength(50)]
    public string Tag { get; set; } = string.Empty;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;
}

public class DocumentShare
{
    [Key]
    public int DocumentShareId { get; set; }

    [Required]
    public int DocumentId { get; set; }

    /// <summary>Exactly one of SharedWithUserId / SharedWithDepartment is set.</summary>
    public int? SharedWithUserId { get; set; }

    [MaxLength(100)]
    public string? SharedWithDepartment { get; set; }

    [Required]
    public int SharedByUserId { get; set; }

    public DateTime SharedDate { get; set; } = DateTime.UtcNow;

    [ForeignKey("DocumentId")]
    public virtual Document Document { get; set; } = null!;

    [ForeignKey("SharedWithUserId")]
    public virtual User? SharedWithUser { get; set; }

    [ForeignKey("SharedByUserId")]
    public virtual User SharedByUser { get; set; } = null!;
}

/// <summary>
/// Append-only audit record. DocumentId deliberately has NO foreign key so the record
/// survives deletion of the document; title and type are snapshots.
/// </summary>
public class DocumentActivity
{
    [Key]
    public int DocumentActivityId { get; set; }

    public int DocumentId { get; set; }

    [Required]
    [MaxLength(255)]
    public string DocumentTitle { get; set; } = string.Empty;

    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    /// <summary>One of <see cref="DocumentActionType"/>.</summary>
    [Required]
    [MaxLength(20)]
    public string ActionType { get; set; } = string.Empty;

    public int UserId { get; set; }

    public DateTime ActivityDate { get; set; } = DateTime.UtcNow;

    [MaxLength(500)]
    public string? Details { get; set; }
}
