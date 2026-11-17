using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using SnackCounter.Domain.Common;

namespace SnackCounter.Application.Abstractions;

public interface IRequest<TResult>;

/// <summary>Changes state. One handler per command, one folder per use case.</summary>
public interface ICommand<TResult> : IRequest<TResult>;

/// <summary>Reads state, never changes it.</summary>
public interface IQuery<TResult> : IRequest<TResult>;

public interface IRequestHandler<in TRequest, TResult> where TRequest : IRequest<TResult>
{
    Task<Result<TResult>> Handle(TRequest request, CancellationToken ct);
}

public interface IDispatcher
{
    Task<Result<TResult>> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default);
}

/// <summary>
/// A deliberately tiny mediator: resolves the one handler for a request from DI. No reflection per call
/// after the first, no licence concerns. Cross-cutting behaviour (logging, transactions) is added by
/// decorating <see cref="IRequestHandler{TRequest,TResult}"/> registrations.
/// </summary>
public sealed class Dispatcher(IServiceProvider services) : IDispatcher
{
    private static readonly ConcurrentDictionary<Type, object> Invokers = new();

    public Task<Result<TResult>> Send<TResult>(IRequest<TResult> request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var invoker = (RequestInvoker<TResult>)Invokers.GetOrAdd(
            request.GetType(),
            type => Activator.CreateInstance(typeof(RequestInvoker<,>).MakeGenericType(type, typeof(TResult)))!);
        return invoker.Invoke(request, services, ct);
    }

    private abstract class RequestInvoker<TResult>
    {
        public abstract Task<Result<TResult>> Invoke(IRequest<TResult> request, IServiceProvider services, CancellationToken ct);
    }

    private sealed class RequestInvoker<TRequest, TResult> : RequestInvoker<TResult> where TRequest : IRequest<TResult>
    {
        public override Task<Result<TResult>> Invoke(IRequest<TResult> request, IServiceProvider services, CancellationToken ct) =>
            services.GetRequiredService<IRequestHandler<TRequest, TResult>>().Handle((TRequest)request, ct);
    }
}
