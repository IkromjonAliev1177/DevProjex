namespace DevProjex.Tests.Integration.Helpers;

internal static class SharedGitRepositories
{
    private static readonly SemaphoreSlim Sync = new(1, 1);
    private static SharedGitRepositoriesState? _state;
    private static int _cleanupRegistered;

    public static async Task<bool> IsGitAvailableAsync()
        => (await GetStateAsync()).IsGitAvailable;

    public static async Task<GitTestRepository?> GetDefaultRepositoryAsync()
        => (await GetStateAsync()).DefaultRepository;

    public static async Task<GitTestRepository?> GetLargeRepositoryAsync()
        => (await GetStateAsync()).LargeRepository;

    // All Git integration tests clone from these immutable bare repositories into
    // their own temp folders, so sharing them is safe and removes repeated setup cost.
    private static async Task<SharedGitRepositoriesState> GetStateAsync()
    {
        if (_state is not null)
            return _state;

        await Sync.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_state is not null)
                return _state;

            var service = new GitRepositoryService();
            var isGitAvailable = await service.IsGitAvailableAsync().ConfigureAwait(false);

            GitTestRepository? defaultRepository = null;
            GitTestRepository? largeRepository = null;

            if (isGitAvailable)
            {
                defaultRepository = await GitTestRepository.CreateAsync().ConfigureAwait(false);
                largeRepository = await GitTestRepository.CreateAsync(includeLargePayload: true).ConfigureAwait(false);
            }

            _state = new SharedGitRepositoriesState(isGitAvailable, defaultRepository, largeRepository);
            RegisterCleanup();
            return _state;
        }
        finally
        {
            Sync.Release();
        }
    }

    private static void RegisterCleanup()
    {
        if (Interlocked.Exchange(ref _cleanupRegistered, 1) == 1)
            return;

        AppDomain.CurrentDomain.ProcessExit += static (_, _) => DisposeSharedState();
    }

    private static void DisposeSharedState()
    {
        var state = Interlocked.Exchange(ref _state, null);
        state?.Dispose();
    }

    private sealed class SharedGitRepositoriesState(
        bool isGitAvailable,
        GitTestRepository? defaultRepository,
        GitTestRepository? largeRepository) : IDisposable
    {
        public bool IsGitAvailable { get; } = isGitAvailable;

        public GitTestRepository? DefaultRepository { get; } = defaultRepository;

        public GitTestRepository? LargeRepository { get; } = largeRepository;

        public void Dispose()
        {
            LargeRepository?.Dispose();
            DefaultRepository?.Dispose();
        }
    }
}
