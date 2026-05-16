namespace ApiGateway.Endpoints;

internal static class ApiFaults
{
    public static async Task ApplyAsync(string? faultMode, ILogger logger, CancellationToken cancellationToken)
    {
        switch (faultMode)
        {
            case "api-error":
                logger.LogWarning("Injecting API fault mode {FaultMode}", faultMode);
                throw new InvalidOperationException("Injected API error for demo/testing.");
            case "api-slow":
                logger.LogWarning("Injecting API delay fault mode {FaultMode}", faultMode);
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                break;
        }
    }
}
