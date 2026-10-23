using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class Granma
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public class CreateGranmaRequest
{
    [Required]
    [MaxLength(255)]
    public string Name { get; set; } = string.Empty;
}
