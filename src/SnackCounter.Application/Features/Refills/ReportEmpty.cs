using SnackCounter.Application.Abstractions;
using SnackCounter.Application.Services;
using SnackCounter.Domain.Common;
using SnackCounter.Domain.Refills;

namespace SnackCounter.Application.Features.Refills;

/// <summary>Anyone reports an empty box, from chat, the web app or the QR code on the box.</summary>
public sealed record ReportEmpty(CounterId CounterId, EmployeeId ReportedBy) : ICommand<ReportEmptyResult>;

public sealed record ReportEmptyResult(RefillId RefillId, bool AlreadyReported, RefillStatus Status, EmployeeId? AssigneeId);

public sealed class ReportEmptyHandler(
    IOrganisationRepository organisation,
    IRefillRepository refills,
    RefillAssigner assigner,
    IUnitOfWork unitOfWork,
    TimeProvider time) : IRequestHandler<ReportEmpty, ReportEmptyResult>
{
    public async Task<Result<ReportEmptyResult>> Handle(ReportEmpty command, CancellationToken ct)
    {
        var counter = await organisation.GetCounter(command.CounterId, ct);
        if (counter is null) return AppErrors.CounterNotFound(command.CounterId);
        var office = await organisation.GetOffice(counter.OfficeId, ct);
        if (office is null) return AppErrors.OfficeNotFound(counter.OfficeId);
        if (await organisation.GetEmployee(command.ReportedBy, ct) is null) return AppErrors.EmployeeNotFound(command.ReportedBy);

        // Rule: no duplicate refills. The repository (a unique index in PostgreSQL) is the real guard,
        // this check just gives a friendly answer in the common case.
        if (await refills.GetActiveForCounter(counter.Id, ct) is { } existing) return AlreadyReported(existing);

        var refill = Refill.Report(counter.Id, office.Id, office.Currency, command.ReportedBy, time.GetUtcNow());
        try
        {
            refills.Add(refill);
            await assigner.AssignNext(refill, office, ct);
            await unitOfWork.SaveChanges(ct);
        }
        catch (DuplicateActiveRefillException)
        {
            // Two reports raced and the other one won. Report theirs instead of failing.
            var winner = await refills.GetActiveForCounter(counter.Id, ct);
            return winner is null ? RefillErrors.ConcurrentUpdate : AlreadyReported(winner);
        }

        return new ReportEmptyResult(refill.Id, false, refill.Status, refill.AssigneeId);
    }

    private static ReportEmptyResult AlreadyReported(Refill refill) => new(refill.Id, true, refill.Status, refill.AssigneeId);
}
