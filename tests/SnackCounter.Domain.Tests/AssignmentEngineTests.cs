using Shouldly;
using SnackCounter.Domain.Assignment;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using static SnackCounter.Domain.Tests.TestData;

namespace SnackCounter.Domain.Tests;

public class AssignmentEngineTests
{
    private static readonly Dictionary<string, double> DefaultWeights = new()
    {
        ["FewestRecentRefills"] = 0.5,
        ["LongestSinceLastRefill"] = 0.3,
        ["InOfficeToday"] = 0.2,
    };

    private readonly AssignmentEngine _engine = AssignmentEngine.CreateDefault();

    private static Candidate Volunteer(
        EmployeeId? id = null, OfficeId? home = null, bool isVolunteer = true,
        Presence? today = null, Presence? nextWorkingDay = null,
        int recentRefills = 0, DateTimeOffset? lastRefillAt = null, int lifetimeRefills = 0, int unpaidClaims = 0) =>
        new(id ?? EmployeeId.New(), home ?? Oslo.Id, isVolunteer,
            today ?? Presence.At(Oslo.Id), nextWorkingDay ?? Presence.At(Oslo.Id),
            recentRefills, lastRefillAt, lifetimeRefills, unpaidClaims);

    private static AssignmentContext OsloContext(
        IReadOnlySet<EmployeeId>? alreadyAttempted = null, bool allowVisitors = true, Dictionary<string, double>? weights = null) =>
        new(Oslo.Id, allowVisitors, alreadyAttempted ?? new HashSet<EmployeeId>(), UnpaidClaimsCap: 2, weights ?? DefaultWeights, Now);

    [Fact]
    public void Excludes_people_on_holiday_or_working_remotely()
    {
        var onHoliday = Volunteer(today: Presence.OnHoliday, nextWorkingDay: Presence.OnHoliday);
        var remote = Volunteer(today: Presence.WorkingRemotely, nextWorkingDay: Presence.WorkingRemotely);
        var inOffice = Volunteer();

        var decision = _engine.Rank([onHoliday, remote, inOffice], OsloContext());

        decision.Ranked.ShouldHaveSingleItem().Candidate.ShouldBe(inOffice);
        decision.Excluded.Select(e => e.Rule).ShouldAllBe(rule => rule == "MustBePresentSoon");
    }

    [Fact]
    public void Someone_only_in_tomorrow_is_eligible_but_ranked_below_someone_in_today()
    {
        var tomorrow = Volunteer(today: Presence.WorkingRemotely);
        var today = Volunteer();

        var decision = _engine.Rank([tomorrow, today], OsloContext());

        decision.Ranked.Select(r => r.Candidate).ShouldBe([today, tomorrow]);
    }

    [Fact]
    public void Visitor_is_eligible_at_the_office_they_are_visiting()
    {
        var visitor = Volunteer(home: Bengaluru.Id, today: Presence.At(Oslo.Id), nextWorkingDay: Presence.At(Oslo.Id));

        _engine.Rank([visitor], OsloContext()).Top!.Candidate.ShouldBe(visitor);
    }

    [Fact]
    public void Oslo_volunteer_visiting_Bengaluru_is_not_asked_to_refill_Oslo()
    {
        var away = Volunteer(today: Presence.At(Bengaluru.Id), nextWorkingDay: Presence.At(Bengaluru.Id));

        _engine.Rank([away], OsloContext()).Top.ShouldBeNull();
    }

    [Fact]
    public void Visitors_are_excluded_when_the_office_does_not_allow_them()
    {
        var visitor = Volunteer(home: Bengaluru.Id, today: Presence.At(Oslo.Id));

        var decision = _engine.Rank([visitor], OsloContext(allowVisitors: false));

        decision.Excluded.ShouldHaveSingleItem().Rule.ShouldBe("MustServeCounterOffice");
    }

    [Fact]
    public void Prefers_the_volunteer_with_the_fewest_recent_refills()
    {
        var busy = Volunteer(recentRefills: 4, lastRefillAt: Now.AddDays(-3));
        var rested = Volunteer(recentRefills: 1, lastRefillAt: Now.AddDays(-3));

        _engine.Rank([busy, rested], OsloContext()).Top!.Candidate.ShouldBe(rested);
    }

    [Fact]
    public void With_equal_recent_refills_prefers_whoever_refilled_longest_ago()
    {
        var recent = Volunteer(recentRefills: 1, lastRefillAt: Now.AddDays(-5));
        var longAgo = Volunteer(recentRefills: 1, lastRefillAt: Now.AddDays(-40));

        _engine.Rank([recent, longAgo], OsloContext()).Top!.Candidate.ShouldBe(longAgo);
    }

    [Fact]
    public void Ties_are_broken_deterministically_by_lifetime_refills_then_id()
    {
        var a = Volunteer(id: new EmployeeId(Guid.Parse("00000000-0000-0000-0000-00000000000a")), lifetimeRefills: 5);
        var b = Volunteer(id: new EmployeeId(Guid.Parse("00000000-0000-0000-0000-00000000000b")), lifetimeRefills: 2);
        var c = Volunteer(id: new EmployeeId(Guid.Parse("00000000-0000-0000-0000-00000000000c")), lifetimeRefills: 2);

        var forwards = _engine.Rank([a, b, c], OsloContext()).Ranked.Select(r => r.Candidate);
        var backwards = _engine.Rank([c, b, a], OsloContext()).Ranked.Select(r => r.Candidate);

        forwards.ShouldBe([b, c, a]);
        backwards.ShouldBe([b, c, a]);
    }

    [Fact]
    public void Skips_volunteers_who_already_wait_on_too_many_unpaid_claims()
    {
        var owed = Volunteer(unpaidClaims: 2);
        var fine = Volunteer(unpaidClaims: 1, recentRefills: 3);

        var decision = _engine.Rank([owed, fine], OsloContext());

        decision.Top!.Candidate.ShouldBe(fine);
        decision.Excluded.ShouldHaveSingleItem().Rule.ShouldBe("UnpaidClaimsBelowCap");
    }

    [Fact]
    public void Never_re_offers_someone_who_already_had_a_turn()
    {
        var declined = Volunteer();

        var decision = _engine.Rank([declined], OsloContext(alreadyAttempted: new HashSet<EmployeeId> { declined.Id }));

        decision.Top.ShouldBeNull();
        decision.Excluded.ShouldHaveSingleItem().Rule.ShouldBe("MustNotHaveBeenAttempted");
    }

    [Fact]
    public void Non_volunteers_are_never_chosen() =>
        _engine.Rank([Volunteer(isVolunteer: false)], OsloContext()).Top.ShouldBeNull();

    [Fact]
    public void Weights_are_configuration_not_code()
    {
        var neverRefilledButRemoteToday = Volunteer(today: Presence.WorkingRemotely, recentRefills: 0);
        var busyButHereToday = Volunteer(recentRefills: 3, lastRefillAt: Now.AddDays(-1));

        _engine.Rank([neverRefilledButRemoteToday, busyButHereToday], OsloContext())
            .Top!.Candidate.ShouldBe(neverRefilledButRemoteToday);

        var presenceFirst = new Dictionary<string, double> { ["InOfficeToday"] = 10 };
        _engine.Rank([neverRefilledButRemoteToday, busyButHereToday], OsloContext(weights: presenceFirst))
            .Top!.Candidate.ShouldBe(busyButHereToday);
    }

    [Fact]
    public void Explanation_tells_the_candidate_why_they_were_picked() =>
        _engine.Rank([Volunteer()], OsloContext()).Top!.Explanation
            .ShouldBe("0 refills in the last 90 days, never refilled before, in the office today");
}

public class PresenceResolverTests
{
    private static readonly DateOnly Tuesday = new(2026, 11, 17);
    private static readonly DateOnly Saturday = new(2026, 11, 21);

    [Fact]
    public void Default_office_days_decide_when_nothing_is_declared()
    {
        var employee = Employee(Oslo.Id, DayOfWeek.Monday, DayOfWeek.Wednesday);

        PresenceResolver.Resolve(employee, [], Tuesday).ShouldBe(Presence.WorkingRemotely);
        PresenceResolver.Resolve(employee, [], Tuesday.AddDays(1)).ShouldBe(Presence.At(Oslo.Id));
    }

    [Fact]
    public void Declared_holiday_overrides_office_days()
    {
        var employee = Employee(Oslo.Id);
        var holiday = Availability.Create(employee.Id, Tuesday, Tuesday.AddDays(7), AvailabilityKind.Holiday).ShouldSucceed();

        PresenceResolver.Resolve(employee, [holiday], Tuesday).ShouldBe(Presence.OnHoliday);
    }

    [Fact]
    public void Visiting_another_office_puts_you_at_that_office()
    {
        var employee = Employee(Bengaluru.Id);
        var visit = Availability.Create(employee.Id, Tuesday, Tuesday.AddDays(10), AvailabilityKind.AtOffice, Oslo.Id).ShouldSucceed();

        PresenceResolver.Resolve(employee, [visit], Tuesday).ShouldBe(Presence.At(Oslo.Id));
    }

    [Fact]
    public void Weekends_are_never_working_days()
    {
        var employee = Employee(Oslo.Id, DayOfWeek.Saturday);

        PresenceResolver.Resolve(employee, [], Saturday).ShouldBe(Presence.NotWorking);
        WorkCalendar.NextWorkingDay(new DateOnly(2026, 11, 20)).ShouldBe(new DateOnly(2026, 11, 23));
    }

    [Fact]
    public void Office_presence_must_name_an_office() =>
        Availability.Create(EmployeeId.New(), Tuesday, Tuesday, AvailabilityKind.AtOffice)
            .ShouldFailWith("availability.office_required");
}
