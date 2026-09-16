using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;
using Workflow.Api.Models;

namespace Workflow.Api.Controllers;

[ApiController]
[Route("api/workflows")]
[Produces("application/json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
public class WorkflowsController(WorkflowDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<WorkflowResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<WorkflowResponse[]>> List(CancellationToken cancellationToken)
    {
        var rows = await db.Workflows.AsNoTracking().OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .ToListAsync(cancellationToken);
        return rows.Select(WorkflowResponse.From).ToArray();
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType<WorkflowResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WorkflowResponse>> Get(Guid id, CancellationToken cancellationToken)
    {
        var row = await db.Workflows.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        return row is null ? Problem(statusCode: 404, title: "Workflow not found") : WorkflowResponse.From(row);
    }

    [HttpPost]
    [ProducesResponseType<WorkflowResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WorkflowResponse>> Create(CreateWorkflowRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            ModelState.AddModelError(nameof(request.Name), "A non-blank name is required.");
            return ValidationProblem(ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        var row = new WorkflowDefinition
        {
            Id = Guid.NewGuid(), Name = request.Name.Trim(), Description = request.Description?.Trim(),
            CreatedAt = now, UpdatedAt = now
        };
        db.Workflows.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = row.Id }, WorkflowResponse.From(row));
    }
}
