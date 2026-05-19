namespace Application.Common.Exceptions;

public sealed class UnreachableSelectedBinsException : InvalidOperationException
{
    public UnreachableSelectedBinsException()
        : base("Unable to build route: graph contains unreachable selected bins.")
    {
    }
}