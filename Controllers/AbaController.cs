using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AbaController : ControllerBase
{
    private readonly AbaStore _store;

    public AbaController(AbaStore store)
    {
        _store = store;
    }

    // GET: api/aba
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _store.ListAsync());
    }

    // POST: api/aba
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAbaRequest request)
    {
        var name = request.Name.Trim();
        if (name.Length == 0) return BadRequest(new { error = "Name is required." });

        var row = await _store.AddAsync(name);
        return CreatedAtAction(nameof(GetAll), new { id = row.Id }, row);
    }
}
