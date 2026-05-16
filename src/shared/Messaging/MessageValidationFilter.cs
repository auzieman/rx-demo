// /src/shared/Messaging/MessageValidationFilter.cs
using MassTransit;
using System.ComponentModel.DataAnnotations;
using Shared.Messaging;

namespace Shared.Messaging;

public sealed class MessageValidationFilter<T> : IFilter<ConsumeContext<T>>
    where T : class
{
    public void Probe(ProbeContext context) => context.CreateFilterScope("messageValidation");

    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var errors = Validate(context.Message);
        if (errors.Count > 0)
            throw new ValidationException($"{typeof(T).Name} invalid: {string.Join("; ", errors)}");

        await next.Send(context);
    }

    private static List<string> Validate(T msg)
    {
        var e = new List<string>();
        switch (msg)
        {
            case ApprovePrescriptionCommand a:
                if (string.IsNullOrWhiteSpace(a.RxId)) e.Add("RxId required");
                if (string.IsNullOrWhiteSpace(a.ApprovedBy)) e.Add("ApprovedBy required");
                break;
            case RefillRequestCommand r:
                if (string.IsNullOrWhiteSpace(r.RxId)) e.Add("RxId required");
                if (r.RefillCount <= 0) e.Add("RefillCount must be >= 1");
                break;
        }
        return e;
    }
}
