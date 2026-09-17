using System.ComponentModel.DataAnnotations;

namespace Workflow.Api.Models;

public sealed class CreateWorkflowRequest
{
    [Required, StringLength(200), NoNullCharacters]
    public string? Name { get; init; }

    [StringLength(2000), NoNullCharacters]
    public string? Description { get; init; }
}

public sealed record WorkflowResponse(
    Guid Id, string Name, string? Description, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static WorkflowResponse From(WorkflowDefinition workflow) => new(
        workflow.Id, workflow.Name, workflow.Description, workflow.CreatedAt, workflow.UpdatedAt);
}
