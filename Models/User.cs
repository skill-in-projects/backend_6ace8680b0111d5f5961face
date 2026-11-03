namespace Backend.Models;

public class User
{
    public int Id { get; set; }

    public string GoogleId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string? Picture { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class GoogleLoginRequest
{
    // The ID token (a signed "credential" string) that Google Sign-In gives the browser
    public string? Credential { get; set; }
}
