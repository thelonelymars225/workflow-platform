using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Workflow.Api.Data;
using Workflow.Api.Filters;
using Workflow.Api.Models;
using Workflow.Api.Services;

namespace Workflow.Api.Controllers;

// Read-only view of the caller's organization structure, including the nearest-manager fallback.
[ApiController]
[OrgScoped]
[Route("api/departments")]
[Produces("application/json")]
public class DepartmentsController(WorkflowDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<DepartmentResponse[]>(StatusCodes.Status200OK)]
    public async Task<ActionResult<DepartmentResponse[]>> List(CancellationToken cancellationToken)
    {
        var org = HttpContext.GetCallerScope().OrganizationId;
        var departments = await db.Departments.AsNoTracking().Where(x => x.OrganizationId == org)
            .OrderBy(x => x.Path).ToListAsync(cancellationToken);
        var activeUsers = (await db.Memberships.AsNoTracking().Where(x => x.OrganizationId == org && x.IsActive)
            .Select(x => x.UserId).ToListAsync(cancellationToken)).ToHashSet();
        var byId = departments.ToDictionary(x => x.Id);
        return departments.Select(department =>
        {
            var chain = new List<Department> { department };
            for (var parent = department.ParentId; parent is { } p && byId.TryGetValue(p, out var next); parent = next.ParentId)
                chain.Add(next);
            var effective = OrgHierarchyService.ResolveEffectiveManager(chain, activeUsers);
            return new DepartmentResponse(department.Id, department.ParentId, department.Name, department.Path,
                department.Depth, department.ManagerUserId, effective?.UserId);
        }).ToArray();
    }
}
