using Microsoft.AspNetCore.Mvc;
using Backend.Models;
using Backend.Services;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EmployeesController : ControllerBase
{
    private readonly EmployeeStore _store;

    public EmployeesController(EmployeeStore store)
    {
        _store = store;
    }

    // GET: api/employees
    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        return Ok(await _store.ListAsync());
    }

    // POST: api/employees
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateEmployeeRequest request)
    {
        var name = request.Name.Trim();
        var email = request.Email.Trim();
        var department = string.IsNullOrWhiteSpace(request.Department) ? null : request.Department.Trim();
        if (name.Length == 0) return BadRequest(new { error = "Name is required." });
        if (email.Length == 0) return BadRequest(new { error = "Email is required." });

        var employee = await _store.AddAsync(name, email, department);
        return CreatedAtAction(nameof(GetAll), new { id = employee.Id }, employee);
    }
}
