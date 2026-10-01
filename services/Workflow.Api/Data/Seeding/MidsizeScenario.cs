using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// ~50 people: a head office plus 5 departments (2 levels), one manager per department.
public static class MidsizeScenario
{
    public static readonly string[] DepartmentNames = ["Sales", "Operations", "Finance", "Engineering", "People"];

    public static void Build(SeedSet set)
    {
        var random = new Random(2002);
        var org = set.Organization("midsize", "Northwind Logistics", "midsize");
        var ceo = set.User("midsize/ceo", "Jonas Lindqvist");
        var head = set.Department(org, "midsize/head", "Head Office", manager: ceo);
        set.Member(org, ceo, OrgRole.Admin, head);

        var index = 100;
        foreach (var name in DepartmentNames)
        {
            var slug = name.ToLowerInvariant();
            var department = set.Department(org, $"midsize/{slug}", name, head);
            var manager = set.User($"midsize/{slug}/manager", ScenarioHelpers.PersonName(index++));
            set.Member(org, manager, OrgRole.Manager, department);
            set.SetManager(department, manager);
            var staff = new List<Guid> { manager };
            for (var i = 0; i < 9; i++)
            {
                var user = set.User($"midsize/{slug}/{i}", ScenarioHelpers.PersonName(index++));
                set.Member(org, user, OrgRole.Member, department);
                staff.Add(user);
            }
            for (var i = 0; i < 80; i++)
            {
                var owner = staff[random.Next(staff.Count)];
                Guid? assignee = random.Next(4) == 0 ? staff[random.Next(staff.Count)] : null;
                set.Task($"midsize/{slug}/{i}", org, department, owner, ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random), assignee, dayOffset: i);
            }
            var automation = set.Automation($"midsize/{slug}/report", org, department, manager, $"{name} weekly report",
                random.Next(4) == 0 ? AutomationTaskStatus.Paused : AutomationTaskStatus.Active);
            ScenarioHelpers.AddRuns(set, $"midsize/{slug}/report", automation, 12, random, SeedSet.Anchor);
        }
    }
}
