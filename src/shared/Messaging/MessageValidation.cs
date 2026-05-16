using System.ComponentModel.DataAnnotations;

namespace Shared.Messaging;

public static class MessageValidation
{
    public static void ThrowIfInvalid<T>(T message)
        where T : class
    {
        var errors = Validate(message);
        if (errors.Count > 0)
            throw new ValidationException($"{typeof(T).Name} invalid: {string.Join("; ", errors)}");
    }

    public static IReadOnlyList<string> Validate<T>(T message)
        where T : class
    {
        var errors = new List<string>();
        switch (message)
        {
            case ApprovePrescriptionCommand approve:
                Require(errors, approve.RxId, "RxId required");
                Require(errors, approve.ApprovedBy, "ApprovedBy required");
                if (approve.ApprovedAt > DateTimeOffset.UtcNow.AddMinutes(5))
                    errors.Add("ApprovedAt cannot be in the future");
                break;
            case RefillRequestCommand refill:
                Require(errors, refill.RxId, "RxId required");
                if (refill.RefillCount is < 1 or > 12)
                    errors.Add("RefillCount must be between 1 and 12");
                if (refill.RequestedAt > DateTimeOffset.UtcNow.AddMinutes(5))
                    errors.Add("RequestedAt cannot be in the future");
                break;
            case PrescriptionChangedEvent changed:
                Require(errors, changed.RxId, "RxId required");
                Require(errors, changed.Status, "Status required");
                if (changed.Version < 1)
                    errors.Add("Version must be >= 1");
                break;
        }

        return errors;
    }

    private static void Require(List<string> errors, string? value, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(message);
    }
}
