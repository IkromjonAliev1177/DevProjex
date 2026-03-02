namespace DevProjex.Tests.Unit.Avalonia;

public sealed class NameFilterCoordinatorTests
{
    [Fact]
    public void OnNameFilterChanged_StartsDebounceOperation()
    {
        using var coordinator = new NameFilterCoordinator(_ => { });

        coordinator.OnNameFilterChanged();

        var debounceCts = GetDebounceCts(coordinator);
        Assert.NotNull(debounceCts);
        Assert.False(debounceCts!.IsCancellationRequested);
    }

    [Fact]
    public void CancelPending_CancelsDebounceOperation()
    {
        using var coordinator = new NameFilterCoordinator(_ => { });
        coordinator.OnNameFilterChanged();
        var debounceCts = GetDebounceCts(coordinator);

        coordinator.CancelPending();

        Assert.NotNull(debounceCts);
        Assert.True(debounceCts!.IsCancellationRequested);
    }

    [Fact]
    public void CancelPending_CancelsActiveFilterTokenSource()
    {
        using var coordinator = new NameFilterCoordinator(_ => { });
        var cts = new CancellationTokenSource();
        SetFilterCts(coordinator, cts);

        coordinator.CancelPending();

        Assert.True(cts.IsCancellationRequested);
    }

    private static CancellationTokenSource? GetDebounceCts(NameFilterCoordinator coordinator)
    {
        var field = typeof(NameFilterCoordinator).GetField(
            "_debounceCts",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        return field!.GetValue(coordinator) as CancellationTokenSource;
    }

    private static void SetFilterCts(NameFilterCoordinator coordinator, CancellationTokenSource cts)
    {
        var field = typeof(NameFilterCoordinator).GetField(
            "_filterCts",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(field);
        field!.SetValue(coordinator, cts);
    }
}
