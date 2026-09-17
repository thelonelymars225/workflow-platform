using System.ComponentModel.DataAnnotations;

namespace Workflow.Api.Models;

[AttributeUsage(AttributeTargets.Property)]
public sealed class NoNullCharactersAttribute : ValidationAttribute
{
    public NoNullCharactersAttribute() : base("The {0} field must not contain null characters.") { }

    public override bool IsValid(object? value) => value is not string text || !text.Contains('\0');
}
