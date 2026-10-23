using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;
using Npgsql;

namespace Backend.Controllers;

[ApiController]
[Route("api/test/{projectId:int}/notes")]
public class ProjectNotesController : ControllerBase
{
    private readonly ProjectNotesStore _store;

    public ProjectNotesController(ProjectNotesStore store)
    {
        _store = store;
    }

    // GET: api/test/5/notes
    [HttpGet]
    public async Task<IActionResult> GetAll(int projectId)
    {
        if (!await _store.ProjectExistsAsync(projectId)) return NotFound();
        return Ok(await _store.ListAsync(projectId));
    }

    // POST: api/test/5/notes
    [HttpPost]
    public async Task<IActionResult> Create(int projectId, [FromBody] CreateProjectNoteRequest request)
    {
        var text = request.Text.Trim();
        if (text.Length == 0) return BadRequest(new { error = "Note text is required." });

        try
        {
            var note = await _store.AddAsync(projectId, text);
            return CreatedAtAction(nameof(GetAll), new { projectId }, note);
        }
        catch (PostgresException ex) when (ex.SqlState == "23503") // Foreign key violation: project does not exist
        {
            return NotFound();
        }
    }
}
