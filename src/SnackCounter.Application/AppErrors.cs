using SnackCounter.Domain.Common;

namespace SnackCounter.Application;

public static class AppErrors
{
    public static Error OfficeNotFound(OfficeId id) => Error.NotFound("office.not_found", $"Office {id} does not exist.");
    public static Error CounterNotFound(CounterId id) => Error.NotFound("counter.not_found", $"Counter {id} does not exist.");
    public static Error EmployeeNotFound(EmployeeId id) => Error.NotFound("employee.not_found", $"Employee {id} does not exist.");

    public static readonly Error NotAVolunteer =
        Error.Forbidden("employee.not_volunteer", "Only volunteers can take on refills.");

    public static readonly Error NotOfficeAdmin =
        Error.Forbidden("employee.not_office_admin", "Only an admin of this office can do that.");

    public static readonly Error ReceiptRequired =
        Error.Validation("receipt.required", "Attach a photo of the receipt.");

    public static Error InvalidCurrency(string value) =>
        Error.Validation("currency.invalid", $"'{value}' is not a valid currency code.");

    public static readonly Error AvailabilityOverlaps =
        Error.Validation("availability.overlaps", "These dates overlap with something you have already declared.");
}
