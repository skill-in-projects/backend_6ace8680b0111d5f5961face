using Microsoft.AspNetCore.Mvc;
using System.Globalization;
using System.Text.Json;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CityMapController : ControllerBase
{
    /// <summary>
    /// GET /api/citymap?city=Paris - Geocodes the city with the Geocoding API, then returns a PNG
    /// map of it from the Static Maps API. The key stays on the server, so it never reaches the browser.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetMap([FromQuery] string city)
    {
        city = (city ?? string.Empty).Trim();
        if (city.Length == 0) return BadRequest(new { error = "City is required." });
        if (city.Length > 100) return BadRequest(new { error = "City name is too long." });

        var key = GoogleApiKeyHelper.GetMapsApiKey();
        if (string.IsNullOrWhiteSpace(key))
            return StatusCode(503, new { error = "GOOGLE_MAPS_API_KEY is not set." });

        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            // 1. Geocode the city name to coordinates
            var geocodeUrl = $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(city)}&key={Uri.EscapeDataString(key)}";
            using var geocodeResponse = await httpClient.GetAsync(geocodeUrl);
            var geocodeText = await geocodeResponse.Content.ReadAsStringAsync();
            if (!geocodeResponse.IsSuccessStatusCode)
                return StatusCode(502, new { error = "Geocoding request failed.", status = (int)geocodeResponse.StatusCode });

            using var geocodeDoc = JsonDocument.Parse(geocodeText);
            var geocodeStatus = geocodeDoc.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : "";
            if (geocodeStatus == "ZERO_RESULTS")
                return NotFound(new { error = $"No place found for \"{city}\"." });
            if (geocodeStatus != "OK")
                return StatusCode(502, new { error = "Geocoding failed.", status = geocodeStatus });

            var location = geocodeDoc.RootElement.GetProperty("results")[0].GetProperty("geometry").GetProperty("location");
            var lat = location.GetProperty("lat").GetDouble().ToString(CultureInfo.InvariantCulture);
            var lng = location.GetProperty("lng").GetDouble().ToString(CultureInfo.InvariantCulture);

            // 2. Fetch a static map image centred on those coordinates
            var mapUrl = $"https://maps.googleapis.com/maps/api/staticmap?center={lat},{lng}&zoom=11&size=800x500&scale=2&maptype=roadmap&markers=color:red%7C{lat},{lng}&key={Uri.EscapeDataString(key)}";
            using var mapResponse = await httpClient.GetAsync(mapUrl);
            if (!mapResponse.IsSuccessStatusCode)
                return StatusCode(502, new { error = "Static Maps request failed.", status = (int)mapResponse.StatusCode });

            var contentType = mapResponse.Content.Headers.ContentType?.MediaType ?? "image/png";
            var bytes = await mapResponse.Content.ReadAsByteArrayAsync();
            return File(bytes, contentType);
        }
        catch (Exception ex)
        {
            return StatusCode(502, new { error = ex.Message });
        }
    }
}
