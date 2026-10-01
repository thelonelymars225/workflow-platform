using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// ~5 people, one flat department, one admin.
public static class StartupScenario
{
    public static void Build(SeedSet set)
    {
        var random = new Random(1001);
        var org = set.Organization("startup", "Pixel Forge", "startup");
        var team = set.Department(org, "startup/team", "Pixel Forge");
        var founder = set.User("startup/founder", "Maya Chen");
        set.Member(org, founder, OrgRole.Admin, team);
        set.SetManager(team, founder);
        var people = new List<Guid> { founder };
        for (var i = 1; i <= 4; i++)
        {
            var user = set.User($"startup/member-{i}", ScenarioHelpers.PersonName(i * 3));
            set.Member(org, user, OrgRole.Member, team);
            people.Add(user);
        }
        for (var i = 0; i < 25; i++)
        {
            var owner = people[random.Next(people.Count)];
            Guid? assignee = random.Next(3) == 0 ? people[random.Next(people.Count)] : null;
            set.Task($"startup/{i}", org, team, owner, ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random), assignee, dayOffset: i);
        }
        var deploy = set.Automation("startup/deploy", org, team, founder, "Nightly deploy");
        ScenarioHelpers.AddRuns(set, "startup/deploy", deploy, 8, random, SeedSet.Anchor);
        set.Automation("startup/backup", org, team, founder, "Weekly backup", AutomationTaskStatus.Paused);
    }
}
