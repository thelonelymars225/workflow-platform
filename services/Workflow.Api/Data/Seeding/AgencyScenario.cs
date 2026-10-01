using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// ~30 agency staff shared across the agency org and 3 client orgs, with different roles per org.
public static class AgencyScenario
{
    public static readonly Guid AgencyOrg = SeedIds.Of("org/agency");
    public static readonly Guid AgencyDelivery = SeedIds.Of("dept/agency/delivery");
    public static readonly string[] Clients = ["acme", "nimbus", "orbit"];

    public static Guid Staff(int index) => SeedIds.Of($"user/agency/staff-{index}");
    public static Guid ClientOrg(string client) => SeedIds.Of($"org/agency-client/{client}");

    public static void Build(SeedSet set)
    {
        var random = new Random(4004);
        var agency = set.Organization("agency", "Brightline Agency", "agency");
        var root = set.Department(agency, "agency/root", "Brightline Agency");
        var teams = new[] { "creative", "strategy", "delivery" }
            .Select(x => set.Department(agency, $"agency/{x}", char.ToUpperInvariant(x[0]) + x[1..], root)).ToArray();

        var staff = Enumerable.Range(0, 30).Select(i => set.User($"agency/staff-{i}", ScenarioHelpers.PersonName(500 + i))).ToArray();
        // Staff 0 is the agency admin; staff 1-3 manage the three teams; the rest are members.
        set.Member(agency, staff[0], OrgRole.Admin, root);
        set.SetManager(root, staff[0]);
        for (var i = 1; i < staff.Length; i++)
        {
            var team = teams[i % 3];
            set.Member(agency, staff[i], i <= 3 ? OrgRole.Manager : OrgRole.Member, i <= 3 ? teams[i - 1] : team);
            if (i <= 3) set.SetManager(teams[i - 1], staff[i]);
        }
        for (var i = 0; i < 60; i++)
        {
            var team = teams[i % 3];
            var owner = staff[1 + random.Next(staff.Length - 1)];
            set.Task($"agency/{i}", agency, team, owner, ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random), dayOffset: i);
        }
        var billing = set.Automation("agency/billing", agency, teams[2], staff[3], "Monthly client billing");
        ScenarioHelpers.AddRuns(set, "agency/billing", billing, 6, random, SeedSet.Anchor);

        // Each client org gets 10 agency staff. Roles are rotated so the same person differs between orgs:
        // e.g. a plain agency member can be a client Manager, and an agency manager can be a client Member.
        for (var c = 0; c < Clients.Length; c++)
        {
            var client = Clients[c];
            var org = set.Organization($"agency-client/{client}", $"{char.ToUpperInvariant(client[0])}{client[1..]} (client)", $"client-{client}");
            var clientRoot = set.Department(org, $"agency-client/{client}/root", $"{client} HQ");
            var marketing = set.Department(org, $"agency-client/{client}/marketing", "Marketing", clientRoot);
            var digital = set.Department(org, $"agency-client/{client}/digital", "Digital", clientRoot);
            var clientAdmin = set.User($"agency-client/{client}/admin", ScenarioHelpers.PersonName(700 + c));
            set.Member(org, clientAdmin, OrgRole.Admin, clientRoot);
            set.SetManager(clientRoot, clientAdmin);

            var assigned = Enumerable.Range(0, 10).Select(i => staff[(c * 7 + i * 3) % staff.Length]).Distinct().ToList();
            for (var i = 0; i < assigned.Count; i++)
            {
                var role = i switch { 0 => OrgRole.Manager, 1 => OrgRole.Manager, 2 when c == 2 => OrgRole.Admin, _ => OrgRole.Member };
                var department = i % 2 == 0 ? marketing : digital;
                set.Member(org, assigned[i], role, department);
                if (i < 2) set.SetManager(department, assigned[i]);
            }
            for (var i = 0; i < 40; i++)
            {
                var owner = i % 4 == 0 ? clientAdmin : assigned[random.Next(assigned.Count)];
                set.Task($"agency-client/{client}/{i}", org, i % 2 == 0 ? marketing : digital, owner,
                    ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random), dayOffset: i);
            }
        }
    }
}
