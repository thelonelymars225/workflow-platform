using Microsoft.EntityFrameworkCore;

namespace Workflow.Api.Data;

// Configure a database provider and add entity sets when persistence is implemented.
public class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options) : DbContext(options)
{
}
