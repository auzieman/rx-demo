namespace Shared.Observability;

public static class FaultModes
{
    public static readonly string[] ApiModes =
    {
        "api-error",
        "api-slow"
    };

    public static readonly string[] WorkerModes =
    {
        "worker-transient-once",
        "worker-timeout",
        "worker-fail",
        "publish-fail"
    };

    public static readonly string[] ProjectionModes =
    {
        "projection-fail",
        "projection-timeout",
        "cache-fail"
    };
}
