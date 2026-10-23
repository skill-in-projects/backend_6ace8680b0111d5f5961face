using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class GranmasController : ControllerBase
{
    private readonly GranmaStore _store;

    public GranmasController(GranmaStore store)
    {
        _store = store;
    }

    // GET: api/granmas
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _store.ListAsync());
    }

    // POST: api/granmas
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateGranmaRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "Name is required." });

        var granma = await _store.AddAsync(name);
        return CreatedAtAction(nameof(GetAll), new { id = granma.Id }, granma);
    }
}
