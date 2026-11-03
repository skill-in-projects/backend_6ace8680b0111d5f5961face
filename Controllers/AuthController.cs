using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Controllers;

/// <summary>
/// Google sign-in. The browser gets a Google ID token, sends it here, and we check it with Google.
/// After that we hand back our own signed token (JWT) for later requests.
/// Needs two environment variables set in Railway: GOOGLE_CLIENT_ID and JWT_SECRET.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    public const string Issuer = "skill-in-backend";
    public const string Audience = "skill-in-frontend";

    private static readonly TimeSpan TokenLifetime = TimeSpan.FromDays(7);

    private readonly UserStore _store;

    public AuthController(UserStore store)
    {
        _store = store;
    }

    // GET: api/auth/config - tells the frontend which Google Client ID to use (the ID is public, not a secret)
    [HttpGet("config")]
    public IActionResult GetConfig()
    {
        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        return Ok(new
        {
            configured = !string.IsNullOrWhiteSpace(clientId),
            clientId = string.IsNullOrWhiteSpace(clientId) ? null : clientId
        });
    }

    // POST: api/auth/google
    [HttpPost("google")]
    public async Task<IActionResult> LoginWithGoogle([FromBody] GoogleLoginRequest request)
    {
        var clientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
        var secret = Environment.GetEnvironmentVariable("JWT_SECRET");
        if (string.IsNullOrWhiteSpace(clientId)) return StatusCode(503, new { error = "GOOGLE_CLIENT_ID is not set." });
        if (string.IsNullOrWhiteSpace(secret)) return StatusCode(503, new { error = "JWT_SECRET is not set." });
        if (string.IsNullOrWhiteSpace(request.Credential)) return BadRequest(new { error = "Credential is required." });

        // Ask Google to check the token. Google rejects forged or expired tokens with a non-200 status.
        using var http = new HttpClient();
        var tokenInfoUrl = "https://oauth2.googleapis.com/tokeninfo?id_token=" + Uri.EscapeDataString(request.Credential);
        using var googleResponse = await http.GetAsync(tokenInfoUrl);
        if (!googleResponse.IsSuccessStatusCode) return Unauthorized(new { error = "Google could not verify this sign-in." });

        using var json = JsonDocument.Parse(await googleResponse.Content.ReadAsStringAsync());
        var root = json.RootElement;
        var aud = root.GetProperty("aud").GetString();
        var googleId = root.GetProperty("sub").GetString();
        var email = root.TryGetProperty("email", out var emailProp) ? emailProp.GetString() : null;
        var emailVerified = root.TryGetProperty("email_verified", out var verifiedProp) && verifiedProp.ToString() == "true";

        // The token must have been issued for this app, and the email must be verified
        if (aud != clientId) return Unauthorized(new { error = "This sign-in is for a different app." });
        if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email) || !emailVerified)
            return Unauthorized(new { error = "Google did not return a verified email." });

        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() : null;
        var picture = root.TryGetProperty("picture", out var pictureProp) ? pictureProp.GetString() : null;

        var user = await _store.UpsertGoogleUserAsync(googleId, name ?? email, email, picture);
        return Ok(new { token = CreateToken(user, secret), user });
    }

    // GET: api/auth/me - returns the signed-in user, or 401 if there is no valid token
    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me()
    {
        // The JWT middleware renames "sub" to NameIdentifier when it reads the token
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(userId, out var id)) return Unauthorized();

        var user = await _store.GetByIdAsync(id);
        return user == null ? Unauthorized() : Ok(user);
    }

    private static string CreateToken(User user, string secret)
    {
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.Email, user.Email),
                new Claim("name", user.Name)
            },
            expires: DateTime.UtcNow.Add(TokenLifetime),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
