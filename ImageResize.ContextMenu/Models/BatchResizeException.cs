namespace ImageResize.ContextMenu.Models;

public sealed class BatchResizeException : Exception
{
    public int TotalCount { get; }
    public int SuccessCount { get; }
    public IReadOnlyList<(string Path, string Reason)> Failures { get; }

    public BatchResizeException(int totalCount, int successCount, IReadOnlyList<(string Path, string Reason)> failures)
        : base($"{successCount} of {totalCount} succeeded, {failures.Count} failed.")
    {
        TotalCount = totalCount;
        SuccessCount = successCount;
        Failures = failures;
    }
}
