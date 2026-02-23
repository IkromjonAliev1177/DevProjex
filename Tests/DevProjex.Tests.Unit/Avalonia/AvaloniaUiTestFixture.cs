using Avalonia;
using Avalonia.Threading;

namespace DevProjex.Tests.Unit.Avalonia;

[CollectionDefinition("AvaloniaUI", DisableParallelization = true)]
public sealed class AvaloniaUiCollection : ICollectionFixture<AvaloniaUiTestFixture>;

public sealed class AvaloniaUiTestFixture
{
	private static readonly object InitLock = new();
	private static volatile bool _initialized;

	public AvaloniaUiTestFixture()
	{
		EnsureInitialized();
	}

	public static void EnsureInitialized()
	{
		if (_initialized)
			return;

		lock (InitLock)
		{
			if (_initialized)
				return;

			if (global::Avalonia.Application.Current is null)
			{
				AppBuilder.Configure<App>()
					.UsePlatformDetect()
					.SetupWithoutStarting();
			}

			_initialized = true;
		}
	}

	public static T RunOnUiThread<T>(Func<T> action)
	{
		EnsureInitialized();

		if (Dispatcher.UIThread.CheckAccess())
			return action();

		return Dispatcher.UIThread.InvokeAsync(action, DispatcherPriority.Send).GetAwaiter().GetResult();
	}

	public static void RunOnUiThread(Action action)
	{
		RunOnUiThread(() =>
		{
			action();
			return 0;
		});
	}
}
