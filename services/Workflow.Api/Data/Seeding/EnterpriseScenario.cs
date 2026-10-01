using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// ~300 people, 5 levels (root, divisions, departments, teams, sub-teams), thousands of tasks and runs in every status.
public static class EnterpriseScenario
{
    public const int PersonalTaskCount = 8000;
    public const int AutomationTaskCount = 200;

    public static void Build(SeedSet set)
    {
        var random = new Random(3003);
        var org = set.Organization("enterprise", "Globex Corporation", "enterprise");
        var root = set.Department(org, "enterprise/root", "Globex Corporation");
        var departments = new List<Guid> { root };
        string[] divisions = ["Commercial", "Technology", "Operations", "Corporate"];
        string[] units = ["North", "Central", "South"];
        string[] teams = ["Alpha", "Bravo"];
        foreach (var division in divisions)
        {
            var d = set.Department(org, $"enterprise/{division}", division, root);
            departments.Add(d);
            foreach (var unit in units)
            {
                var u = set.Department(org, $"enterprise/{division}/{unit}", $"{division} {unit}", d);
                departments.Add(u);
                foreach (var team in teams)
                {
                    var t = set.Department(org, $"enterprise/{division}/{unit}/{team}", $"{division} {unit} {team}", u);
                    departments.Add(t);
                    if (team == "Alpha")
                        departments.Add(set.Department(org, $"enterprise/{division}/{unit}/{team}/squad", $"{division} {unit} {team} Squad", t));
                }
            }
        }

        // Admins first, then one manager per department, then members spread over departments (deeper ones get more).
        var index = 1000;
        var people = new List<(Guid User, Guid Department)>();
        for (var i = 0; i < 3; i++)
        {
            var admin = set.User($"enterprise/admin-{i}", ScenarioHelpers.PersonName(index++));
            set.Member(org, admin, OrgRole.Admin, root);
            people.Add((admin, root));
            if (i == 0) set.SetManager(root, admin);
        }
        foreach (var department in departments.Skip(1))
        {
            var manager = set.User($"enterprise/manager/{department:N}", ScenarioHelpers.PersonName(index++));
            set.Member(org, manager, OrgRole.Manager, department);
            set.SetManager(department, manager);
            people.Add((manager, department));
        }
        var leafish = departments.Where(d => set.GetDepartment(d).Depth >= 2).ToList();
        for (var i = 0; people.Count < 300; i++)
        {
            var department = leafish[i % leafish.Count];
            var user = set.User($"enterprise/member-{i}", ScenarioHelpers.PersonName(index++));
            set.Member(org, user, OrgRole.Member, department);
            people.Add((user, department));
        }
        var byDepartment = people.GroupBy(x => x.Department).ToDictionary(g => g.Key, g => g.Select(x => x.User).ToList());

        for (var i = 0; i < PersonalTaskCount; i++)
        {
            var department = departments[random.Next(departments.Count)];
            var staff = byDepartment.GetValueOrDefault(department) ?? byDepartment[root];
            var owner = staff[random.Next(staff.Count)];
            // About a fifth of tasks are assigned, sometimes to someone from another department.
            Guid? assignee = random.Next(5) == 0 ? people[random.Next(people.Count)].User : null;
            set.Task($"enterprise/{i}", org, department, owner, ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random),
                assignee, dayOffset: random.Next(270));
        }
        for (var i = 0; i < AutomationTaskCount; i++)
        {
            var department = departments[random.Next(departments.Count)];
            var owner = set.GetDepartment(department).ManagerUserId ?? people[0].User;
            var automation = set.Automation($"enterprise/automation-{i}", org, department, owner, $"Automation {i:000}: {ScenarioHelpers.TaskTitle(random)}",
                random.Next(5) == 0 ? AutomationTaskStatus.Paused : AutomationTaskStatus.Active, dayOffset: random.Next(200));
            ScenarioHelpers.AddRuns(set, $"enterprise/automation-{i}", automation, random.Next(4, 16), random, SeedSet.Anchor.AddDays(random.Next(200)));
        }
    }
}
