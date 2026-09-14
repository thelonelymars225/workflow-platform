namespace Workflow.Contracts;

public record NotificationRequested(
    Guid WorkflowId,
    string RecipientEmail,
    string Subject,
    string Body);
