using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Organisation;
using SnackCounter.Infrastructure.InMemory;
using SnackCounter.Infrastructure.Persistence;

namespace SnackCounter.Infrastructure.Seed;

/// <summary>
/// Demo organisation matching the brief: Oslo (about 15 volunteers, NOK) and Bengaluru (about 20, INR).
/// Ids are fixed so the web app's dev panel and docs can refer to them.
/// </summary>
public static class DemoData
{
    public static readonly OfficeId OsloId = new(Guid.Parse("0a000000-0000-0000-0000-000000000001"));
    public static readonly OfficeId BengaluruId = new(Guid.Parse("0a000000-0000-0000-0000-000000000002"));
    public static readonly CounterId OsloKitchen = new(Guid.Parse("0c000000-0000-0000-0000-000000000001"));
    public static readonly CounterId BengaluruPantry = new(Guid.Parse("0c000000-0000-0000-0000-000000000002"));

    private static readonly string[] OsloNames =
        ["Ingrid", "Lars", "Kari", "Ola", "Nora", "Magnus", "Sofie", "Henrik", "Emma", "Jonas", "Ida", "Sindre", "Thea", "Eirik", "Maja"];

    private static readonly string[] BengaluruNames =
        ["Ravi", "Priya", "Ananya", "Arjun", "Divya", "Karthik", "Meera", "Rohan", "Sneha", "Vikram",
         "Aditi", "Rahul", "Kavya", "Siddharth", "Pooja", "Nikhil", "Lakshmi", "Varun", "Deepa", "Aakash"];

    private static readonly DayOfWeek[][] OfficePatterns =
    [
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday],
        [DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday],
        [DayOfWeek.Monday, DayOfWeek.Wednesday],
        [DayOfWeek.Thursday, DayOfWeek.Friday],
    ];

    public static IReadOnlyList<Office> Offices() =>
    [
        new Office(OsloId, "Oslo", Currency.Of("NOK"), "Europe/Oslo",
            Money.Of(6000, "NOK"), Money.Of(300, "NOK"), Money.Of(1200, "NOK")),
        new Office(BengaluruId, "Bengaluru", Currency.Of("INR"), "Asia/Kolkata",
            Money.Of(15000, "INR"), Money.Of(500, "INR"), Money.Of(3000, "INR")),
    ];

    public static IReadOnlyList<Counter> Counters() =>
    [
        new Counter(OsloKitchen, OsloId, "Kitchen"),
        new Counter(BengaluruPantry, BengaluruId, "Pantry"),
    ];

    /// <summary>The first person in each office is the office admin (not a volunteer); everyone else volunteers.</summary>
    public static IReadOnlyList<Employee> Employees()
    {
        var people = new List<Employee>();
        Add(OsloNames, OsloId, "NOK", 1);
        Add(BengaluruNames, BengaluruId, "INR", 2);
        return people;

        void Add(string[] names, OfficeId office, string payroll, int officeNumber)
        {
            for (var i = 0; i < names.Length; i++)
            {
                var id = new EmployeeId(Guid.Parse(string.Create(CultureInfo.InvariantCulture, $"0e000000-0000-0000-{officeNumber:D4}-{i + 1:D12}")));
                var isAdmin = i == 0;
                people.Add(new Employee(
                    id, names[i], $"HR-{names[i].ToUpperInvariant()}", names[i].ToLowerInvariant(), office, Currency.Of(payroll),
                    OfficePatterns[i % OfficePatterns.Length].ToHashSet(), IsVolunteer: !isAdmin, IsOfficeAdmin: isAdmin));
            }
        }
    }

    public static void SeedInMemory(InMemoryDatabase db)
    {
        lock (db.Sync)
        {
            if (db.Offices.Count > 0) return;
            db.Offices.AddRange(Offices());
            db.Counters.AddRange(Counters());
            db.Employees.AddRange(Employees());
        }
    }

    public static async Task SeedPostgres(SnackDbContext db, CancellationToken ct = default)
    {
        if (await db.Offices.AnyAsync(ct)) return;
        db.Offices.AddRange(Offices());
        db.Counters.AddRange(Counters());
        db.Employees.AddRange(Employees());
        await db.SaveChangesAsync(ct);
    }
}
