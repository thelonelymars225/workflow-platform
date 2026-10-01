using Workflow.Api.Models;

namespace Workflow.Api.Data.Seeding;

// Stable IDs for every quirk in the messy scenario, so tests and manual checks can refer to them by name.
public static class MessyFixtures
{
    public static readonly Guid Organization = SeedIds.Of("org/messy");
    public const string OrganizationName = "Al-Noor Holding / مجموعة النور";

    public static class Departments
    {
        public static readonly Guid Root = Of("root");
        public static readonly Guid Operations = Of("operations");                 // M1: top-level "Operations"
        public static readonly Guid Procurement = Of("operations/procurement");    // M3: managed by DualManager
        public static readonly Guid Sales = Of("sales");
        public static readonly Guid SalesOperations = Of("sales/operations");      // M1: second "Operations", one level deeper
        public static readonly Guid SalesRetail = Of("sales/retail");              // M2: no manager, falls back to Sales
        public static readonly Guid Finance = Of("finance");
        public static readonly Guid FinanceAnalytics = Of("finance/analytics");
        public static readonly Guid Legal = Of("legal");
        public static readonly Guid LegalInvestigations = Of("legal/investigations"); // M5: confidential
        public static readonly Guid HumanResources = Of("hr");                     // M4: manager on leave
        public static readonly Guid Projects = Of("projects");
        public static readonly Guid ProjectsGulf = Of("projects/gulf");
        public static readonly Guid ProjectsSaudi = Of("projects/gulf/ksa");
        public static readonly Guid ProjectsRiyadh = Of("projects/gulf/ksa/riyadh");
        public static readonly Guid ProjectsInfrastructure = Of("projects/gulf/ksa/riyadh/infrastructure");
        public static readonly Guid ProjectsMetroLine3 = Of("projects/gulf/ksa/riyadh/infrastructure/metro-3"); // M13: depth 6, M3
        public static readonly Guid Archive = Of("archive");                       // M14: empty
        public static readonly Guid AgencyFieldDelivery = SeedIds.Of("dept/agency/delivery/field"); // M8, in the agency org

        private static Guid Of(string key) => SeedIds.Of($"dept/messy/{key}");
    }

    public static class People
    {
        public static readonly Guid Ceo = Of("ceo");
        public static readonly Guid AuditorAdmin = Of("auditor");                  // M5: admin denied on Legal Investigations
        public static readonly Guid OperationsDirector = Of("ops-director");
        public static readonly Guid DualManager = Of("dual-manager");              // M3
        public static readonly Guid SalesDirector = Of("sales-director");
        public static readonly Guid SalesOperationsLead = Of("sales-ops-lead");    // M12: plain member in Retail
        public static readonly Guid FinanceDirector = Of("finance-director");
        public static readonly Guid FinanceAnalyst = Of("finance-analyst");        // M6: View grant on Sales
        public static readonly Guid LegalDirector = Of("legal-director");          // M5 / M15
        public static readonly Guid InvestigationsLead = Of("investigations-lead");
        public static readonly Guid HrManagerOnLeave = Of("hr-manager");           // M4
        public static readonly Guid ActingHrManager = Of("acting-hr");             // M4: active delegation
        public static readonly Guid ExpiredActingHrManager = Of("expired-acting-hr"); // M4: delegation ended last month
        public static readonly Guid ProjectsDirector = Of("projects-director");
        public static readonly Guid GrantAndDenyUser = Of("grant-and-deny");       // M7
        public static readonly Guid Contractor = Of("contractor");                 // M8
        public static readonly Guid Deactivated = Of("deactivated");               // M9
        public static readonly Guid Mover = Of("mover");                           // M10
        public static readonly Guid OutsideAssignee = Of("outside-assignee");      // M11
        public static readonly Guid LongName = Of("long-name");                    // M16
        public static readonly Guid SameNameOperations = Of("same-name-ops");      // M16
        public static readonly Guid SameNameSales = Of("same-name-sales");         // M16
        public static readonly Guid RetailMember = Of("retail-member");
        public static readonly Guid HrMember = Of("hr-member");

        public const string SharedName = "Mohammed Ali / محمد علي";
        public const string LongDisplayName =
            "Abdul-Rahman bin Mohammed bin Abdullah bin Abdulaziz Al-Fulani Al-Qahtani Al-Shammari / " +
            "عبد الرحمن بن محمد بن عبد الله بن عبد العزيز الفلاني القحطاني الشمري";

        private static Guid Of(string key) => SeedIds.Of($"user/messy/{key}");
    }

    public static class Tasks
    {
        public static readonly Guid TopOperations = Of("ops-top");                 // M1
        public static readonly Guid SalesOperations = Of("ops-sales");             // M1
        public static readonly Guid Retail = Of("retail");                         // M2 / M12
        public static readonly Guid Procurement = Of("procurement");               // M3
        public static readonly Guid MetroLine3 = Of("metro-3");                    // M3 / M13
        public static readonly Guid HumanResources = Of("hr");                     // M4
        public static readonly Guid Investigations = Of("investigations");         // M5
        public static readonly Guid InvestigationsOwnedByLegalDirector = Of("investigations-legal-director"); // M15
        public static readonly Guid Legal = Of("legal");
        public static readonly Guid Sales = Of("sales");                           // M6
        public static readonly Guid ContractorMessy = Of("contractor-messy");      // M8
        public static readonly Guid ContractorAgency = SeedIds.Of("task/agency/contractor-field"); // M8
        public static readonly Guid DeactivatedOwned1 = Of("deactivated-1");       // M9
        public static readonly Guid DeactivatedOwned2 = Of("deactivated-2");       // M9
        public static readonly Guid MoverOld1 = Of("mover-old-1");                 // M10
        public static readonly Guid MoverOld2 = Of("mover-old-2");                 // M10
        public static readonly Guid MoverNew = Of("mover-new");                    // M10
        public static readonly Guid AssignedOutsideDepartment = Of("legal-assigned-outside"); // M11
        public static readonly Guid SalesOpsLeadOwnRetail = Of("retail-own-sales-ops-lead"); // M12
        public static readonly Guid GrantAndDenyHr = Of("hr-2");                   // M7
        public static readonly Guid SameNameOperations = Of("same-name-ops");      // M16
        public static readonly Guid SameNameSales = Of("same-name-sales");         // M16
        public static readonly Guid LongName = Of("long-name");                    // M16
        public static readonly Guid SalesAutomation = SeedIds.Of("automation/messy/sales-digest");
        public static readonly Guid SalesAutomationFailedRun = SeedIds.Of("run/messy/sales-digest/failed");

        private static Guid Of(string key) => SeedIds.Of($"task/messy/{key}");
    }
}

// Al-Noor Holding / مجموعة النور: ~45 people and every awkward case M1-M16 (see MessyFixtures).
public static class MessyScenario
{
    public static void Build(SeedSet set)
    {
        // M8 needs the agency org; seeding it here keeps "messy" self-contained (and idempotent with "agency").
        AgencyScenario.Build(set);
        var random = new Random(5005);
        var org = set.Organization("messy", MessyFixtures.OrganizationName, "al-noor");
        string D(string key) => $"messy/{key}";

        var root = set.Department(org, D("root"), MessyFixtures.OrganizationName);
        var operations = set.Department(org, D("operations"), "Operations", root);
        var procurement = set.Department(org, D("operations/procurement"), "Procurement / المشتريات", operations);
        var sales = set.Department(org, D("sales"), "Sales / المبيعات", root);
        var salesOperations = set.Department(org, D("sales/operations"), "Operations", sales);
        var retail = set.Department(org, D("sales/retail"), "Retail Sales", sales);
        var finance = set.Department(org, D("finance"), "Finance / المالية", root);
        var analytics = set.Department(org, D("finance/analytics"), "Financial Analytics", finance);
        var legal = set.Department(org, D("legal"), "Legal / الشؤون القانونية", root);
        var investigations = set.Department(org, D("legal/investigations"), "Legal Investigations", legal);
        var hr = set.Department(org, D("hr"), "Human Resources / الموارد البشرية", root);
        var projects = set.Department(org, D("projects"), "Projects / المشاريع", root);
        var gulf = set.Department(org, D("projects/gulf"), "Gulf Region", projects);
        var saudi = set.Department(org, D("projects/gulf/ksa"), "Saudi Arabia / السعودية", gulf);
        var riyadh = set.Department(org, D("projects/gulf/ksa/riyadh"), "Riyadh Office", saudi);
        var infrastructure = set.Department(org, D("projects/gulf/ksa/riyadh/infrastructure"), "Infrastructure", riyadh);
        var metro = set.Department(org, D("projects/gulf/ksa/riyadh/infrastructure/metro-3"), "Metro Line 3 / المترو", infrastructure);
        set.Department(org, D("archive"), "Archive", root); // M14: no people, no tasks

        Guid Person(string key, string name, OrgRole role, Guid department, Guid? manages = null, bool active = true)
        {
            var user = set.User($"messy/{key}", name);
            set.Member(org, user, role, department, active);
            if (manages is { } m) set.SetManager(m, user);
            return user;
        }

        var ceo = Person("ceo", "Khalid bin Saeed Al-Mansour / خالد بن سعيد المنصور", OrgRole.Admin, root, manages: root);
        var auditor = Person("auditor", "Huda Al-Rashid", OrgRole.Admin, root);
        var opsDirector = Person("ops-director", "Ahmed Karimi", OrgRole.Manager, operations, manages: operations);
        var dualManager = Person("dual-manager", "Omar Haddad / عمر حداد", OrgRole.Manager, procurement, manages: procurement);
        set.SetManager(metro, dualManager); // M3: second department in a different branch
        var salesDirector = Person("sales-director", "Rania Saleh / رانيا صالح", OrgRole.Manager, sales, manages: sales);
        var salesOpsLead = Person("sales-ops-lead", "Bilal Qureshi", OrgRole.Manager, salesOperations, manages: salesOperations);
        set.ExtraDepartment(org, salesOpsLead, retail); // M12
        var financeDirector = Person("finance-director", "Mona El-Sayed", OrgRole.Manager, finance, manages: finance);
        var analyst = Person("finance-analyst", "Fatima Al-Zahra / فاطمة الزهراء", OrgRole.Member, analytics);
        var legalDirector = Person("legal-director", "Samir Nasser", OrgRole.Manager, legal, manages: legal);
        var investigationsLead = Person("investigations-lead", "Dana Aziz", OrgRole.Manager, investigations, manages: investigations);
        var hrManager = Person("hr-manager", "Laila Hamdan / ليلى حمدان", OrgRole.Manager, hr, manages: hr);
        var actingHr = Person("acting-hr", "Sara Al-Otaibi", OrgRole.Member, hr);
        var expiredActingHr = Person("expired-acting-hr", "Tariq Mahmoud", OrgRole.Member, hr);
        var projectsDirector = Person("projects-director", "Youssef Benali", OrgRole.Manager, projects, manages: projects);
        var grantAndDeny = Person("grant-and-deny", "Layla Faris", OrgRole.Member, operations);
        var contractor = Person("contractor", "Daniel Okafor (contractor)", OrgRole.Member, riyadh);
        var deactivated = Person("deactivated", "Yusuf Rahman", OrgRole.Member, retail, active: false);
        var mover = Person("mover", "Noura Al-Qahtani / نورة القحطاني", OrgRole.Member, finance);
        var outsideAssignee = Person("outside-assignee", "Hassan Jaber", OrgRole.Member, projects);
        var longName = Person("long-name", MessyFixtures.People.LongDisplayName, OrgRole.Member, riyadh);
        var sameNameOps = Person("same-name-ops", MessyFixtures.People.SharedName, OrgRole.Member, operations);
        var sameNameSales = Person("same-name-sales", MessyFixtures.People.SharedName, OrgRole.Member, sales);
        var retailMember = Person("retail-member", "Ali Hassan", OrgRole.Member, retail);
        var hrMember = Person("hr-member", "Reem Khalil / ريم خليل", OrgRole.Member, hr);

        // Fillers bring the org to ~45 people, spread over the non-empty departments.
        string[] fillerNames =
        [
            "Ibrahim Al-Sayegh", "إبراهيم الصايغ", "Grace Mensah", "Zainab Yousef", "Kareem Ali", "Joanna Kowalski", "سلمان العتيبي",
            "Peter O'Neill", "Amira Fathi", "Faisal Al-Harbi", "Lina Haddad", "Rashid Khan", "Mariam Saad / مريم سعد", "Chen Wei",
            "Nadia Bakr", "Hamad Al-Kuwari", "Elif Yilmaz", "Waleed Mansour", "Huda Salem", "Tom Becker", "Aisha Noor"
        ];
        Guid[] fillerHomes = [operations, procurement, sales, salesOperations, retail, finance, analytics, legal, investigations, hr, projects, gulf, saudi, riyadh, infrastructure, metro];
        var staffByDepartment = new Dictionary<Guid, List<Guid>>();
        for (var i = 0; i < fillerNames.Length; i++)
        {
            var home = fillerHomes[i % fillerHomes.Length];
            var user = Person($"filler-{i}", fillerNames[i], OrgRole.Member, home);
            (staffByDepartment.TryGetValue(home, out var list) ? list : staffByDepartment[home] = []).Add(user);
        }

        // Exceptions and delegations. Windows are relative to the seeding time; reset to refresh them.
        var now = set.Now;
        set.Deny("messy/m5-legal-director", org, legalDirector, investigations, reason: "Confidential investigation");
        set.Deny("messy/m5-auditor", org, auditor, investigations, reason: "Confidential investigation");
        set.Grant("messy/m6-analyst-sales", org, analyst, sales, AccessLevel.View, reason: "Revenue analysis");
        set.Grant("messy/m7-grant", org, grantAndDeny, hr, AccessLevel.Edit, reason: "Temporary HR support");
        set.Deny("messy/m7-deny", org, grantAndDeny, hr, reason: "Conflict of interest");
        set.Delegation("messy/m4-active", org, hr, actingHr, hrManager, now.AddDays(-10), now.AddDays(20));
        set.Delegation("messy/m4-expired", org, hr, expiredActingHr, hrManager, now.AddDays(-60), now.AddDays(-30));

        // Named tasks (one per quirk), then a few generic tasks per department.
        string T(string key) => $"messy/{key}";
        set.Task(T("ops-top"), org, operations, opsDirector, "Operations: fleet maintenance plan", PersonalTaskStatus.InProgress);
        set.Task(T("ops-sales"), org, salesOperations, salesOpsLead, "Operations: sales order routing", PersonalTaskStatus.ToDo);
        set.Task(T("retail"), org, retail, retailMember, "Retail: weekend promotion", PersonalTaskStatus.ToDo);
        set.Task(T("procurement"), org, procurement, dualManager, "Procurement: renew steel supplier", PersonalTaskStatus.InProgress);
        set.Task(T("metro-3"), org, metro, longName, "Metro Line 3: station 7 handover / تسليم المحطة", PersonalTaskStatus.ToDo);
        set.Task(T("hr"), org, hr, hrMember, "HR: annual leave calendar", PersonalTaskStatus.ToDo);
        set.Task(T("hr-2"), org, hr, hrManager, "HR: salary bands review", PersonalTaskStatus.InProgress);
        set.Task(T("investigations"), org, investigations, investigationsLead, "Case 2026-114 witness interviews", PersonalTaskStatus.InProgress);
        set.Task(T("investigations-legal-director"), org, investigations, legalDirector, "Case 2026-114 retention notice", PersonalTaskStatus.ToDo);
        set.Task(T("legal"), org, legal, legalDirector, "Legal: contract template refresh", PersonalTaskStatus.Done);
        set.Task(T("sales"), org, sales, salesDirector, "Sales: Q4 targets / أهداف الربع الرابع", PersonalTaskStatus.InProgress);
        set.Task(T("contractor-messy"), org, riyadh, contractor, "Site survey report", PersonalTaskStatus.InProgress);
        set.Task(T("deactivated-1"), org, retail, deactivated, "Retail: stock count (Yusuf)", PersonalTaskStatus.InProgress);
        set.Task(T("deactivated-2"), org, retail, deactivated, "Retail: supplier returns (Yusuf)", PersonalTaskStatus.ToDo);
        set.Task(T("mover-old-1"), org, operations, mover, "Operations: depot inventory (before move)", PersonalTaskStatus.Done, dayOffset: -30);
        set.Task(T("mover-old-2"), org, operations, mover, "Operations: shift roster (before move)", PersonalTaskStatus.InProgress, dayOffset: -20);
        set.Task(T("mover-new"), org, finance, mover, "Finance: expense audit (after move)", PersonalTaskStatus.ToDo, dayOffset: 10);
        set.Task(T("legal-assigned-outside"), org, legal, legalDirector, "Legal: review Riyadh site permits", PersonalTaskStatus.ToDo, assignee: outsideAssignee);
        set.Task(T("retail-own-sales-ops-lead"), org, retail, salesOpsLead, "Retail: cover Friday shift", PersonalTaskStatus.ToDo);
        set.Task(T("same-name-ops"), org, operations, sameNameOps, "Operations: forklift certification", PersonalTaskStatus.ToDo);
        set.Task(T("same-name-sales"), org, sales, sameNameSales, "Sales: key account visit", PersonalTaskStatus.ToDo);
        set.Task(T("long-name"), org, riyadh, longName,
            ("Coordinate the multi-phase handover of the Riyadh Metro Line 3 civil works with the municipality, the consultant, " +
             "the main contractor and all utility owners / تنسيق").PadRight(200, '.')[..200], PersonalTaskStatus.ToDo);

        Guid[] withGenericTasks = [operations, procurement, sales, salesOperations, retail, finance, analytics, legal, investigations, hr, projects, gulf, saudi, riyadh, infrastructure, metro];
        foreach (var department in withGenericTasks)
        {
            var owners = staffByDepartment.GetValueOrDefault(department) ?? [];
            if (set.GetDepartment(department).ManagerUserId is { } manager) owners = [.. owners, manager];
            if (owners.Count == 0) owners = [ceo];
            for (var i = 0; i < 4; i++)
                set.Task($"messy/generic/{department:N}/{i}", org, department, owners[random.Next(owners.Count)],
                    ScenarioHelpers.TaskTitle(random), ScenarioHelpers.TaskStatus(random), dayOffset: random.Next(200));
        }

        var digest = set.Automation("messy/sales-digest", org, sales, salesDirector, "Daily sales digest / الملخص اليومي");
        set.Run("messy/sales-digest/ok", digest, AutomationRunStatus.Succeeded, 1, SeedSet.Anchor.AddDays(1));
        set.Run("messy/sales-digest/failed", digest, AutomationRunStatus.Failed, 1, SeedSet.Anchor.AddDays(2));
        var payroll = set.Automation("messy/hr-payroll", org, hr, hrManager, "Payroll export", AutomationTaskStatus.Paused);
        set.Run("messy/hr-payroll/1", payroll, AutomationRunStatus.Retrying, 1, SeedSet.Anchor.AddDays(3));

        // M8: the contractor is also a Manager in the agency org, running its field delivery team.
        var field = set.Department(AgencyScenario.AgencyOrg, "agency/delivery/field", "Field Delivery", AgencyScenario.AgencyDelivery, contractor);
        set.Member(AgencyScenario.AgencyOrg, contractor, OrgRole.Manager, field);
        set.Task("agency/contractor-field", AgencyScenario.AgencyOrg, field, contractor, "Agency: field kit checklist", PersonalTaskStatus.ToDo);
    }
}
