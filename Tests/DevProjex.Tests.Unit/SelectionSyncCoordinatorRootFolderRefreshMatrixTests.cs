using DevProjex.Application.Models;

namespace DevProjex.Tests.Unit;

public sealed class SelectionSyncCoordinatorRootFolderRefreshMatrixTests
{
	[Theory]
	[MemberData(nameof(ExtensionsFallbackCases))]
	public void ApplyMissingProfileSelectionsFallbackToExtensions_Matrix(
		int caseId,
		string preparedMode,
		bool hasSavedSelections,
		string optionKind,
		bool expectedFallback)
	{
		var coordinator = CreateCoordinator();
		SetPreparedSelectionMode(coordinator, preparedMode);
		SetExtensionsSelectionCache(coordinator, hasSavedSelections ? ["missing"] : []);
		var options = CreateExtensionOptions(optionKind);

		var actual = InvokeApplyMissingProfileSelectionsFallbackToExtensions(coordinator, options);

		Assert.Equal(expectedFallback, !ReferenceEquals(options, actual));
		if (expectedFallback)
		{
			Assert.All(actual, option => Assert.True(option.IsChecked));
			Assert.Equal(options.Select(option => option.Name), actual.Select(option => option.Name));
		}
		else
		{
			Assert.Same(options, actual);
		}

		Assert.True(caseId >= 0);
	}

	[Theory]
	[MemberData(nameof(RootFallbackCases))]
	public void ApplyMissingProfileSelectionsFallbackToRootFolders_Matrix(
		int caseId,
		string preparedMode,
		bool hasSavedSelections,
		bool hasAnyCheckedOption,
		bool ignoreDotFolders,
		bool includeSmartIgnoredFolder,
		bool expectedFallback)
	{
		var coordinator = CreateCoordinator();
		SetPreparedSelectionMode(coordinator, preparedMode);
		SetRootSelectionCache(coordinator, hasSavedSelections ? ["missing-root"] : []);

		var scannedRootFolders = new[] { ".git", "src", "node_modules", "docs" };
		var options = scannedRootFolders
			.Select((name, index) => new SelectionOption(name, hasAnyCheckedOption && index == 0))
			.ToList();

		var ignoreRules = CreateIgnoreRules(ignoreDotFolders, includeSmartIgnoredFolder);

		var actual = InvokeApplyMissingProfileSelectionsFallbackToRootFolders(
			coordinator,
			options,
			scannedRootFolders,
			ignoreRules);

		Assert.Equal(expectedFallback, !ReferenceEquals(options, actual));
		if (expectedFallback)
		{
			var expected = new FilterOptionSelectionService().BuildRootFolderOptions(
				scannedRootFolders,
				new HashSet<string>(StringComparer.OrdinalIgnoreCase),
				ignoreRules,
				hasPreviousSelections: false);

			Assert.Equal(expected.Select(option => option.Name), actual.Select(option => option.Name));
			Assert.Equal(expected.Select(option => option.IsChecked), actual.Select(option => option.IsChecked));
		}
		else
		{
			Assert.Same(options, actual);
		}

		Assert.True(caseId >= 0);
	}

	[Theory]
	[MemberData(nameof(IgnoreFallbackCases))]
	public void ShouldUseIgnoreDefaultFallback_Matrix(
		int caseId,
		string preparedMode,
		string previousSelectionKind,
		string visibleOptionsKind,
		bool expected)
	{
		var coordinator = CreateCoordinator();
		SetPreparedSelectionMode(coordinator, preparedMode);
		var previousSelections = CreatePreviousSelections(previousSelectionKind);
		var visibleOptions = CreateVisibleIgnoreOptions(visibleOptionsKind);

		var actual = InvokeShouldUseIgnoreDefaultFallback(coordinator, visibleOptions, previousSelections);

		Assert.Equal(expected, actual);
		Assert.True(caseId >= 0);
	}

	public static IEnumerable<object[]> ExtensionsFallbackCases()
	{
		var modes = new[] { "None", "Defaults", "Profile" };
		var cacheVariants = new[] { false, true };
		var optionKinds = new[] { "empty", "unchecked_pair", "single_checked", "all_checked" };
		var caseId = 0;

		foreach (var mode in modes)
		{
			foreach (var hasCache in cacheVariants)
			{
				foreach (var optionKind in optionKinds)
				{
					var expectedFallback = mode == "Profile" &&
					                       hasCache &&
					                       optionKind == "unchecked_pair";
					yield return
					[
						caseId++,
						mode,
						hasCache,
						optionKind,
						expectedFallback
					];
				}
			}
		}
	}

	public static IEnumerable<object[]> RootFallbackCases()
	{
		var modes = new[] { "None", "Defaults", "Profile" };
		var cacheVariants = new[] { false, true };
		var checkedVariants = new[] { false, true };
		var dotIgnoreVariants = new[] { false, true };
		var smartIgnoreVariants = new[] { false, true };
		var caseId = 0;

		foreach (var mode in modes)
		{
			foreach (var hasCache in cacheVariants)
			{
				foreach (var hasChecked in checkedVariants)
				{
					foreach (var ignoreDot in dotIgnoreVariants)
					{
						foreach (var smartIgnore in smartIgnoreVariants)
						{
							var expectedFallback = mode == "Profile" && hasCache && !hasChecked;
							yield return
							[
								caseId++,
								mode,
								hasCache,
								hasChecked,
								ignoreDot,
								smartIgnore,
								expectedFallback
							];
						}
					}
				}
			}
		}
	}

	public static IEnumerable<object[]> IgnoreFallbackCases()
	{
		var modes = new[] { "None", "Defaults", "Profile" };
		var previousKinds = new[] { "none", "single", "multi" };
		var visibleKinds = new[] { "empty", "contains_single", "contains_multi", "contains_none" };
		var caseId = 0;

		foreach (var mode in modes)
		{
			foreach (var previousKind in previousKinds)
			{
				foreach (var visibleKind in visibleKinds)
				{
					var previous = CreatePreviousSelections(previousKind);
					var visible = CreateVisibleIgnoreOptions(visibleKind);
					var expected = mode == "Profile" &&
					               previous.Count > 0 &&
					               visible.Count > 0 &&
					               visible.All(option => !previous.Contains(option.Id));

					yield return
					[
						caseId++,
						mode,
						previousKind,
						visibleKind,
						expected
					];
				}
			}
		}
	}

	private static List<SelectionOption> CreateExtensionOptions(string optionKind)
	{
		return optionKind switch
		{
			"empty" => [],
			"unchecked_pair" => [new SelectionOption(".cs", false), new SelectionOption(".md", false)],
			"single_checked" => [new SelectionOption(".cs", true), new SelectionOption(".md", false)],
			"all_checked" => [new SelectionOption(".cs", true), new SelectionOption(".md", true)],
			_ => throw new ArgumentOutOfRangeException(nameof(optionKind), optionKind, "Unknown option kind.")
		};
	}

	private static IgnoreRules CreateIgnoreRules(bool ignoreDotFolders, bool includeSmartIgnoredFolder)
	{
		var smartIgnoredFolders = includeSmartIgnoredFolder
			? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "node_modules" }
			: new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		return new IgnoreRules(
			IgnoreHiddenFolders: false,
			IgnoreHiddenFiles: false,
			IgnoreDotFolders: ignoreDotFolders,
			IgnoreDotFiles: false,
			SmartIgnoredFolders: smartIgnoredFolders,
			SmartIgnoredFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase));
	}

	private static HashSet<IgnoreOptionId> CreatePreviousSelections(string kind)
	{
		return kind switch
		{
			"none" => [],
			"single" => [IgnoreOptionId.HiddenFolders],
			"multi" => [IgnoreOptionId.UseGitIgnore, IgnoreOptionId.SmartIgnore],
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown previous selection kind.")
		};
	}

	private static List<IgnoreOptionDescriptor> CreateVisibleIgnoreOptions(string kind)
	{
		return kind switch
		{
			"empty" => [],
			"contains_single" =>
			[
				new IgnoreOptionDescriptor(IgnoreOptionId.HiddenFolders, "Hidden folders", false),
				new IgnoreOptionDescriptor(IgnoreOptionId.DotFiles, "Dot files", false)
			],
			"contains_multi" =>
			[
				new IgnoreOptionDescriptor(IgnoreOptionId.SmartIgnore, "Smart ignore", false),
				new IgnoreOptionDescriptor(IgnoreOptionId.DotFolders, "Dot folders", false)
			],
			"contains_none" =>
			[
				new IgnoreOptionDescriptor(IgnoreOptionId.DotFolders, "Dot folders", false),
				new IgnoreOptionDescriptor(IgnoreOptionId.HiddenFiles, "Hidden files", false)
			],
			_ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown visible options kind.")
		};
	}

	private static IReadOnlyList<SelectionOption> InvokeApplyMissingProfileSelectionsFallbackToExtensions(
		SelectionSyncCoordinator coordinator,
		IReadOnlyList<SelectionOption> options)
	{
		var method = typeof(SelectionSyncCoordinator).GetMethod(
			"ApplyMissingProfileSelectionsFallbackToExtensions",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);

		var result = method!.Invoke(coordinator, [options]);
		Assert.NotNull(result);
		return (IReadOnlyList<SelectionOption>)result!;
	}

	private static IReadOnlyList<SelectionOption> InvokeApplyMissingProfileSelectionsFallbackToRootFolders(
		SelectionSyncCoordinator coordinator,
		IReadOnlyList<SelectionOption> options,
		IReadOnlyList<string> scannedRootFolders,
		IgnoreRules rules)
	{
		var method = typeof(SelectionSyncCoordinator).GetMethod(
			"ApplyMissingProfileSelectionsFallbackToRootFolders",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);

		var result = method!.Invoke(coordinator, [options, scannedRootFolders, rules]);
		Assert.NotNull(result);
		return (IReadOnlyList<SelectionOption>)result!;
	}

	private static bool InvokeShouldUseIgnoreDefaultFallback(
		SelectionSyncCoordinator coordinator,
		IReadOnlyList<IgnoreOptionDescriptor> options,
		IReadOnlySet<IgnoreOptionId> previousSelections)
	{
		var method = typeof(SelectionSyncCoordinator).GetMethod(
			"ShouldUseIgnoreDefaultFallback",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(method);

		var result = method!.Invoke(coordinator, [options, previousSelections]);
		Assert.NotNull(result);
		return (bool)result!;
	}

	private static void SetPreparedSelectionMode(SelectionSyncCoordinator coordinator, string modeName)
	{
		var field = typeof(SelectionSyncCoordinator).GetField(
			"_preparedSelectionMode",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);

		var value = Enum.Parse(field!.FieldType, modeName);
		field.SetValue(coordinator, value);
	}

	private static void SetExtensionsSelectionCache(SelectionSyncCoordinator coordinator, IReadOnlyCollection<string> values)
	{
		var field = typeof(SelectionSyncCoordinator).GetField(
			"_extensionsSelectionCache",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);

		field!.SetValue(coordinator, new HashSet<string>(values, StringComparer.OrdinalIgnoreCase));
	}

	private static void SetRootSelectionCache(SelectionSyncCoordinator coordinator, IReadOnlyCollection<string> values)
	{
		var field = typeof(SelectionSyncCoordinator).GetField(
			"_rootSelectionCache",
			BindingFlags.Instance | BindingFlags.NonPublic);
		Assert.NotNull(field);

		field!.SetValue(coordinator, new HashSet<string>(values, StringComparer.OrdinalIgnoreCase));
	}

	private static SelectionSyncCoordinator CreateCoordinator()
	{
		var viewModel = CreateViewModel();
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		var scanner = new StubFileSystemScanner();
		var scanOptions = new ScanOptionsUseCase(scanner);
		var filterSelectionService = new FilterOptionSelectionService();
		var ignoreOptionsService = new IgnoreOptionsService(localization);

		return new SelectionSyncCoordinator(
			viewModel,
			scanOptions,
			filterSelectionService,
			ignoreOptionsService,
			(_, _, _) => CreateIgnoreRules(ignoreDotFolders: false, includeSmartIgnoredFolder: false),
			(_, _) => new IgnoreOptionsAvailability(false, false),
			_ => false,
			() => @"C:\Workspace\ProjectA");
	}

	private static MainWindowViewModel CreateViewModel()
	{
		var localization = new LocalizationService(CreateCatalog(), AppLanguage.En);
		return new MainWindowViewModel(localization, new HelpContentProvider());
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
				["Settings.Ignore.DotFolders"] = "Dot folders",
				["Settings.Ignore.DotFiles"] = "Dot files",
				["Settings.Ignore.EmptyFolders"] = "Empty folders",
				["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
			}
		};

		return new StubLocalizationCatalog(data);
	}
}
