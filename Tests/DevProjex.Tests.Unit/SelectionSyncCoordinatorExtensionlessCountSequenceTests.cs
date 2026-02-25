namespace DevProjex.Tests.Unit;

public sealed class SelectionSyncCoordinatorExtensionlessCountSequenceTests
{
	[Theory]
	[MemberData(nameof(CountSequenceCases))]
	public void ApplyExtensionScan_CountSequence_UpdatesVisibilityAndLabel(
		int caseId,
		string[][] scans,
		int[] expectedCounts)
	{
		_ = caseId;
		const string projectPath = @"C:\Workspace\ProjectA";
		var viewModel = CreateViewModel();
		var coordinator = CreateCoordinator(viewModel, () => projectPath);

		for (var i = 0; i < scans.Length; i++)
		{
			coordinator.ApplyExtensionScan(scans[i]);
			viewModel.AllIgnoreChecked = false;
			coordinator.PopulateIgnoreOptionsForRootSelection(Array.Empty<string>(), projectPath);

			AssertExtensionlessOptionState(viewModel, expectedCounts[i], expectChecked: false);
		}
	}

	[Theory]
	[MemberData(nameof(CountSequenceCases))]
	public void ApplyExtensionScan_CountSequence_WithSavedProfile_RestoresCheckedStateWhenOptionReappears(
		int caseId,
		string[][] scans,
		int[] expectedCounts)
	{
		_ = caseId;
		const string projectPath = @"C:\Workspace\ProjectA";
		var viewModel = CreateViewModel();
		var coordinator = CreateCoordinator(viewModel, () => projectPath);
		var profile = new ProjectSelectionProfile(
			SelectedRootFolders: Array.Empty<string>(),
			SelectedExtensions: Array.Empty<string>(),
			SelectedIgnoreOptions: new[] { IgnoreOptionId.ExtensionlessFiles });

		coordinator.ApplyProjectProfileSelections(projectPath, profile);

		for (var i = 0; i < scans.Length; i++)
		{
			coordinator.ApplyExtensionScan(scans[i]);
			coordinator.PopulateIgnoreOptionsForRootSelection(Array.Empty<string>(), projectPath);

			AssertExtensionlessOptionState(viewModel, expectedCounts[i], expectChecked: expectedCounts[i] > 0);
		}
	}

	[Fact]
	public void ApplyExtensionScan_FromRootSelectionScan_CountTracksSelectedRootFolders()
	{
		const string projectPath = @"C:\Workspace\ProjectA";
		var ignoreRules = new IgnoreRules(
			IgnoreHiddenFolders: false,
			IgnoreHiddenFiles: false,
			IgnoreDotFolders: false,
			IgnoreDotFiles: false,
			SmartIgnoredFolders: new HashSet<string>(),
			SmartIgnoredFiles: new HashSet<string>());
		var scanner = new StubFileSystemScanner
		{
			GetRootFileExtensionsHandler = (_, _) => new ScanResult<HashSet<string>>(
				new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Dockerfile", ".cs" },
				false,
				false),
			GetExtensionsHandler = (path, _) => path switch
			{
				@"C:\Workspace\ProjectA\src" => new ScanResult<HashSet<string>>(
					new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Makefile", ".ts" },
					false,
					false),
				@"C:\Workspace\ProjectA\tests" => new ScanResult<HashSet<string>>(
					new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "LICENSE", ".cs" },
					false,
					false),
				_ => new ScanResult<HashSet<string>>(new HashSet<string>(), false, false)
			}
		};

		var viewModel = CreateViewModel();
		var coordinator = CreateCoordinator(viewModel, () => projectPath, scanner);
		var scanOptions = new ScanOptionsUseCase(scanner);

		var rootOnlyScan = scanOptions.GetExtensionsForRootFolders(projectPath, Array.Empty<string>(), ignoreRules);
		coordinator.ApplyExtensionScan(rootOnlyScan.Value);
		viewModel.AllIgnoreChecked = false;
		coordinator.PopulateIgnoreOptionsForRootSelection(Array.Empty<string>(), projectPath);
		AssertExtensionlessOptionState(viewModel, expectedCount: 1, expectChecked: false);

		var srcScan = scanOptions.GetExtensionsForRootFolders(projectPath, new[] { "src" }, ignoreRules);
		coordinator.ApplyExtensionScan(srcScan.Value);
		viewModel.AllIgnoreChecked = false;
		coordinator.PopulateIgnoreOptionsForRootSelection(new[] { "src" }, projectPath);
		AssertExtensionlessOptionState(viewModel, expectedCount: 2, expectChecked: false);

		var srcAndTestsScan = scanOptions.GetExtensionsForRootFolders(projectPath, new[] { "src", "tests" }, ignoreRules);
		coordinator.ApplyExtensionScan(srcAndTestsScan.Value);
		viewModel.AllIgnoreChecked = false;
		coordinator.PopulateIgnoreOptionsForRootSelection(new[] { "src", "tests" }, projectPath);
		AssertExtensionlessOptionState(viewModel, expectedCount: 3, expectChecked: false);
	}

	public static IEnumerable<object[]> CountSequenceCases()
	{
		var patterns = new Dictionary<int, (string[] Scan, int Count)>
		{
			[0] = (new[] { ".cs", ".json", ".md" }, 0),
			[1] = (new[] { "Dockerfile", ".cs" }, 1),
			[2] = (new[] { "Dockerfile", "Makefile", ".cs" }, 2),
			[3] = (new[] { ".env", ".gitignore", ".dockerignore" }, 0),
			[4] = (new[] { "file.", ".txt" }, 1),
			[5] = (new[] { "Dockerfile", "LICENSE", "WORKSPACE" }, 3),
			[6] = (new[] { "archive.tar.gz", ".yaml" }, 0),
			[7] = (new[] { "Taskfile", ".log" }, 1)
		};

		var sequences = new[]
		{
			new[] { 1, 0, 1 },
			new[] { 2, 0, 5 },
			new[] { 0, 0, 0 },
			new[] { 3, 0, 3 },
			new[] { 4, 6, 4 },
			new[] { 1, 2, 5 },
			new[] { 5, 2, 1 },
			new[] { 0, 7, 0 },
			new[] { 7, 0, 2 },
			new[] { 2, 6, 0 },
			new[] { 1, 3, 6 },
			new[] { 5, 0, 4 },
			new[] { 4, 0, 2 },
			new[] { 7, 3, 7 },
			new[] { 2, 1, 0 },
			new[] { 6, 1, 6 },
			new[] { 0, 5, 0 },
			new[] { 3, 7, 3 },
			new[] { 5, 5, 5 },
			new[] { 0, 2, 0 }
		};

		var caseId = 0;
		foreach (var sequence in sequences)
		{
			var scans = sequence.Select(id => patterns[id].Scan).ToArray();
			var counts = sequence.Select(id => patterns[id].Count).ToArray();
			yield return [ caseId++, scans, counts ];
		}
	}

	private static void AssertExtensionlessOptionState(
		MainWindowViewModel viewModel,
		int expectedCount,
		bool expectChecked)
	{
		var option = viewModel.IgnoreOptions.FirstOrDefault(item => item.Id == IgnoreOptionId.ExtensionlessFiles);
		if (expectedCount <= 0)
		{
			Assert.Null(option);
			return;
		}

		Assert.NotNull(option);
		Assert.Equal($"Files without extension ({expectedCount})", option!.Label);
		Assert.Equal(expectChecked, option.IsChecked);
	}

	private static SelectionSyncCoordinator CreateCoordinator(
		MainWindowViewModel viewModel,
		Func<string?> currentPathProvider,
		StubFileSystemScanner? scanner = null)
	{
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		scanner ??= new StubFileSystemScanner();
		var scanOptions = new ScanOptionsUseCase(scanner);
		var filterSelection = new FilterOptionSelectionService();
		var ignoreOptions = new IgnoreOptionsService(localization);

		return new SelectionSyncCoordinator(
			viewModel,
			scanOptions,
			filterSelection,
			ignoreOptions,
			(_, _, _) => new IgnoreRules(
				IgnoreHiddenFolders: false,
				IgnoreHiddenFiles: false,
				IgnoreDotFolders: false,
				IgnoreDotFiles: false,
				SmartIgnoredFolders: new HashSet<string>(),
				SmartIgnoredFiles: new HashSet<string>()),
			(_, _) => new IgnoreOptionsAvailability(IncludeGitIgnore: false, IncludeSmartIgnore: false),
			_ => false,
			currentPathProvider);
	}

	private static MainWindowViewModel CreateViewModel()
	{
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		var viewModel = new MainWindowViewModel(localization, new HelpContentProvider())
		{
			AllIgnoreChecked = false
		};

		return viewModel;
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
				["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
			}
		};

		return new StubLocalizationCatalog(data);
	}
}
