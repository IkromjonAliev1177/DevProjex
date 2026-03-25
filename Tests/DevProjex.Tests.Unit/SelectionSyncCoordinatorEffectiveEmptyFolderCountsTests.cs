using DevProjex.Application.Models;

namespace DevProjex.Tests.Unit;

public sealed class SelectionSyncCoordinatorEffectiveEmptyFolderCountsTests
{
	[Theory]
	[MemberData(nameof(ExtensionSelectionCases))]
	public void ResolveEffectiveIgnoreOptionCounts_ReplacesRawEmptyFoldersWithEffectiveCount(
		string scenario,
		IReadOnlyList<SelectionOption> extensionOptions,
		int expectedEmptyFolders)
	{
		using var temp = new TemporaryDirectory();
		temp.CreateFolder("src");

		var viewModel = CreateViewModel();
		var scanner = new EffectiveCountAwareScanner();
		using var coordinator = CreateCoordinator(viewModel, scanner, temp.Path);

		var resolvedCounts = ResolveEffectiveCounts(
			coordinator,
			temp.Path,
			extensionOptions,
			rawCounts: new IgnoreOptionCounts(EmptyFolders: 99));

		Assert.Equal(expectedEmptyFolders, resolvedCounts.EmptyFolders);
		Assert.Equal(99, resolvedCounts.HiddenFolders);
		Assert.Equal(expectedEmptyFolders, scanner.LastEffectiveCount);
		var expectedAllowedExtensions = scenario switch
		{
			"all-visible" => [".cs", ".md"],
			"markdown-hidden" => [".cs"],
			"all-unchecked" => Array.Empty<string>(),
			_ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, null)
		};
		Assert.True(scanner.LastAllowedExtensions.SequenceEqual(expectedAllowedExtensions));
	}

	[Theory]
	[MemberData(nameof(ExtensionSelectionCases))]
	public void PopulateIgnoreOptionsForRootSelection_UsesEffectiveEmptyFoldersCountResolvedFromSelection(
		string scenario,
		IReadOnlyList<SelectionOption> extensionOptions,
		int expectedEmptyFolders)
	{
		using var temp = new TemporaryDirectory();
		temp.CreateFolder("src");

		var viewModel = CreateViewModel();
		var scanner = new EffectiveCountAwareScanner();
		using var coordinator = CreateCoordinator(viewModel, scanner, temp.Path);
		Assert.False(string.IsNullOrWhiteSpace(scenario));

		var resolvedCounts = ResolveEffectiveCounts(
			coordinator,
			temp.Path,
			extensionOptions,
			rawCounts: new IgnoreOptionCounts(EmptyFolders: 99));
		ApplyScanState(coordinator, extensionOptions, resolvedCounts, hasIgnoreCounts: true);
		coordinator.PopulateIgnoreOptionsForRootSelection(["src"], temp.Path);

		var option = viewModel.IgnoreOptions.SingleOrDefault(item => item.Id == IgnoreOptionId.EmptyFolders);
		if (expectedEmptyFolders == 0)
		{
			Assert.Null(option);
			return;
		}

		Assert.NotNull(option);
		Assert.Equal($"Empty folders ({expectedEmptyFolders})", option!.Label);
		Assert.True(option.IsChecked);
	}

	public static IEnumerable<object[]> ExtensionSelectionCases()
	{
		yield return
		[
			"all-visible",
			new List<SelectionOption>
			{
				new(".cs", true),
				new(".md", true)
			},
			0
		];

		yield return
		[
			"markdown-hidden",
			new List<SelectionOption>
			{
				new(".cs", true),
				new(".md", false)
			},
			2
		];

		yield return
		[
			"all-unchecked",
			new List<SelectionOption>
			{
				new(".cs", false),
				new(".md", false)
			},
			3
		];
	}

	private static IgnoreOptionCounts ResolveEffectiveCounts(
		SelectionSyncCoordinator coordinator,
		string currentPath,
		IReadOnlyList<SelectionOption> extensionOptions,
		IgnoreOptionCounts rawCounts)
	{
		var method = typeof(SelectionSyncCoordinator).GetMethod(
			"ResolveEffectiveIgnoreOptionCounts",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);

		var result = method!.Invoke(
			coordinator,
			[
				currentPath,
				new List<string> { "src" },
				extensionOptions,
				CreateRules(),
				new IgnoreOptionCounts(HiddenFolders: 99, EmptyFolders: rawCounts.EmptyFolders),
				false,
				CancellationToken.None
			]);

		return Assert.IsType<IgnoreOptionCounts>(result);
	}

	private static void ApplyScanState(
		SelectionSyncCoordinator coordinator,
		IReadOnlyCollection<SelectionOption> options,
		IgnoreOptionCounts ignoreCounts,
		bool hasIgnoreCounts)
	{
		var method = typeof(SelectionSyncCoordinator).GetMethod(
			"ApplyExtensionOptions",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);
		method!.Invoke(coordinator, [options, 0, ignoreCounts, hasIgnoreCounts]);
	}

	private static IgnoreRules CreateRules() => new(
		IgnoreHiddenFolders: false,
		IgnoreHiddenFiles: false,
		IgnoreDotFolders: false,
		IgnoreDotFiles: false,
		SmartIgnoredFolders: new HashSet<string>(),
		SmartIgnoredFiles: new HashSet<string>());

	private static MainWindowViewModel CreateViewModel()
	{
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		return new MainWindowViewModel(localization, new HelpContentProvider());
	}

	private static SelectionSyncCoordinator CreateCoordinator(
		MainWindowViewModel viewModel,
		EffectiveCountAwareScanner scanner,
		string currentPath)
	{
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		var filterSelectionService = new FilterOptionSelectionService();
		var ignoreOptionsService = new IgnoreOptionsService(localization);

		return new SelectionSyncCoordinator(
			viewModel,
			new ScanOptionsUseCase(scanner),
			filterSelectionService,
			ignoreOptionsService,
			(_, _, _) => CreateRules(),
			(_, _) => new IgnoreOptionsAvailability(
				IncludeGitIgnore: false,
				IncludeSmartIgnore: false,
				ShowAdvancedCounts: true),
			_ => false,
			() => currentPath);
	}

	private static StubLocalizationCatalog CreateCatalog()
	{
		var data = new Dictionary<AppLanguage, IReadOnlyDictionary<string, string>>
		{
			[AppLanguage.En] = new Dictionary<string, string>
			{
				["Settings.Ignore.SmartIgnore"] = "Smart ignore",
				["Settings.Ignore.UseGitIgnore"] = "Use .gitignore",
				["Settings.Ignore.HiddenFolders"] = "Hidden folders",
				["Settings.Ignore.HiddenFiles"] = "Hidden files",
				["Settings.Ignore.DotFolders"] = "dot folders",
				["Settings.Ignore.DotFiles"] = "dot files",
				["Settings.Ignore.EmptyFolders"] = "Empty folders",
				["Settings.Ignore.EmptyFiles"] = "Empty files",
				["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
			}
		};

		return new StubLocalizationCatalog(data);
	}

	private sealed class EffectiveCountAwareScanner
		: IFileSystemScanner,
			IFileSystemScannerAdvanced,
			IFileSystemScannerEffectiveEmptyFolderCounter
	{
		public string[] LastAllowedExtensions { get; private set; } = [];
		public int LastEffectiveCount { get; private set; }

		public bool CanReadRoot(string rootPath) => true;

		public ScanResult<HashSet<string>> GetExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
			=> new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, false);

		public ScanResult<HashSet<string>> GetRootFileExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
			=> new(new HashSet<string>(StringComparer.OrdinalIgnoreCase), false, false);

		public ScanResult<List<string>> GetRootFolderNames(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
			=> new(["src"], false, false);

		public ScanResult<ExtensionsScanData> GetExtensionsWithIgnoreOptionCounts(
			string rootPath,
			IgnoreRules rules,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(
					new HashSet<string>(StringComparer.OrdinalIgnoreCase)
					{
						".cs",
						".md"
					},
					new IgnoreOptionCounts(EmptyFolders: 99)),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		public ScanResult<ExtensionsScanData> GetRootFileExtensionsWithIgnoreOptionCounts(
			string rootPath,
			IgnoreRules rules,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(
					new HashSet<string>(StringComparer.OrdinalIgnoreCase),
					IgnoreOptionCounts.Empty),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		public ScanResult<int> GetEffectiveEmptyFolderCount(
			string rootPath,
			IReadOnlySet<string> allowedExtensions,
			IgnoreRules rules,
			CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();

			LastAllowedExtensions = allowedExtensions
				.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
				.ToArray();
			LastEffectiveCount = allowedExtensions.Count switch
			{
				0 => 3,
				1 when allowedExtensions.Contains(".cs") => 2,
				_ => 0
			};

			return new ScanResult<int>(LastEffectiveCount, RootAccessDenied: false, HadAccessDenied: false);
		}
	}
}
