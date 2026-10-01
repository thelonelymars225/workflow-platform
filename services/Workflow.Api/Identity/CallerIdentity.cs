namespace Workflow.Api.Identity;

public sealed record CallerIdentity(Guid UserId, Guid OrganizationId);

// The single seam for authentication: replace the registered implementation when real auth arrives.
public interface ICallerIdentityProvider
{
    CallerIdentity? GetCaller(HttpContext context);
}

// Development only: trusts the X-Demo-User-Id and X-Organization-Id headers.
public sealed class DevelopmentHeaderCallerIdentityProvider : ICallerIdentityProvider
{
    public const string UserHeader = "X-Demo-User-Id";
    public const string OrganizationHeader = "X-Organization-Id";

    public CallerIdentity? GetCaller(HttpContext context) =>
        Guid.TryParse(context.Request.Headers[UserHeader].ToString(), out var user)
        && Guid.TryParse(context.Request.Headers[OrganizationHeader].ToString(), out var organization)
            ? new CallerIdentity(user, organization)
            : null;
}

// Outside Development there is no authentication yet, so nobody is identified and every task request gets 401.
public sealed class NoCallerIdentityProvider : ICallerIdentityProvider
{
    public CallerIdentity? GetCaller(HttpContext context) => null;
}
