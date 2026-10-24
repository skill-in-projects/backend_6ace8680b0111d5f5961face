using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CityDescriptionController : ControllerBase
{
    /// <summary>
    /// GET /api/citydescription?city=Paris - Asks Gemini for a description of the place (about 80 words).
    /// The key stays on the server, so it never reaches the browser.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetDescription([FromQuery] string city)
    {
        city = (city ?? string.Empty).Trim();
        if (city.Length == 0) return BadRequest(new { error = "City is required." });
        if (city.Length > 100) return BadRequest(new { error = "City name is too long." });

        var key = GoogleApiKeyHelper.GetGoogleApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(503, new { error = "GOOGLE_API_KEY is not set." });

        try
        {
            var prompt = $"Describe the place \"{city}\" in about 80 words. Write one plain paragraph with no headings or lists. " +
                         "Cover what it is known for and what a visitor would see there.";
            var body = new
            {
                contents = new[] { new { parts = new[] { new { text = prompt } } } },
                // Thinking is turned off so the whole token budget goes to the description.
                generationConfig = new { maxOutputTokens = 400, thinkingConfig = new { thinkingBudget = 0 } }
            };

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(20);
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={Uri.EscapeDataString(key)}";
            var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(url, content);
            var responseText = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                return StatusCode(502, new { error = "Gemini request failed.", status = (int)response.StatusCode });

            using var doc = JsonDocument.Parse(responseText);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()?.Trim();
            if (string.IsNullOrEmpty(text))
                return StatusCode(502, new { error = "Gemini returned no description." });

            return Ok(new { city, description = text });
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
