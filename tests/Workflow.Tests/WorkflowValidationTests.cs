using System.ComponentModel.DataAnnotations;
using Workflow.Api.Models;

namespace Workflow.Tests;

public class WorkflowValidationTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RejectsMissingName(string? name)
    {
        var request = new CreateWorkflowRequest { Name = name };
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }

    [Fact]
    public void RejectsOverlongFields()
    {
        var request = new CreateWorkflowRequest { Name = new string('n', 201), Description = new string('d', 2001) };
        var errors = new List<ValidationResult>();
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, true));
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void AcceptsNameAndOptionalDescription()
    {
        var request = new CreateWorkflowRequest { Name = "Example" };
        Assert.True(Validator.TryValidateObject(request, new ValidationContext(request), [], true));
    }
}
