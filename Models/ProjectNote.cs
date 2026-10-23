using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class ProjectNote
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    public string Text { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateProjectNoteRequest
{
    [Required]
    [MaxLength(2000)]
    public string Text { get; set; } = string.Empty;
}
