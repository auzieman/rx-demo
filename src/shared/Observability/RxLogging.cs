using Microsoft.Extensions.Logging;

namespace Shared.Observability;

public static class RxLogging
{
    public static IDisposable? BeginEventScope(
        this ILogger logger,
        string domain,
        string name,
        params (string Key, object? Value)[] attributes)
    {
        var scope = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["event.domain"] = domain,
            ["event.name"] = name
        };

        foreach (var (key, value) in attributes)
        {
            if (!string.IsNullOrWhiteSpace(key) && value is not null)
                scope[key] = value;
        }

        return logger.BeginScope(scope);
    }
}
