namespace DevProjex.Tests.Unit;

public sealed class SelectionSyncCoordinatorDynamicIgnoreNegativeMatrixTests
{
	private const string ProjectPath = @"C:\Workspace\ProjectA";
	private const string NextProjectPath = @"C:\Workspace\ProjectB";

	[Theory]
	[MemberData(nameof(DynamicOptionIds))]
	public void DynamicIgnoreNegativeMatrix_UnavailableOption_DoesNotAppearAfterVisibleOptionChanges(
		IgnoreOptionId dynamicOptionId)
	{
		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: false);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);
		coordinator.HookIgnoreListeners(viewModel.IgnoreOptions);

		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);
		GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked = false;
		GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked = true;
		GetIgnoreOption(viewModel, IgnoreOptionId.DotFiles).IsChecked = false;

		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);

		Assert.DoesNotContain(viewModel.IgnoreOptions, option => option.Id == dynamicOptionId);
		Assert.False(viewModel.AllIgnoreChecked);
	}

	[Theory]
	[MemberData(nameof(DynamicOptionIds))]
	public void DynamicIgnoreNegativeMatrix_HiddenUncheckedOption_DoesNotFlipToCheckedWhenVisibleOptionsAreAllChecked(
		IgnoreOptionId dynamicOptionId)
	{
		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);
		coordinator.HookIgnoreListeners(viewModel.IgnoreOptions);

		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);
		GetIgnoreOption(viewModel, dynamicOptionId).IsChecked = false;

		availability = BuildAvailability(dynamicOptionId, dynamicVisible: false);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);

		Assert.DoesNotContain(viewModel.IgnoreOptions, option => option.Id == dynamicOptionId);
		Assert.True(viewModel.AllIgnoreChecked);

		availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);

		Assert.False(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
		Assert.False(viewModel.AllIgnoreChecked);
	}

	[Theory]
	[MemberData(nameof(DynamicOptionIds))]
	public void DynamicIgnoreNegativeMatrix_EmptyProfile_DoesNotAutoCheckDynamicOptionWhenItAppears(
		IgnoreOptionId dynamicOptionId)
	{
		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: false);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);

		var profile = new ProjectSelectionProfile(
			SelectedRootFolders: [],
			SelectedExtensions: [],
			SelectedIgnoreOptions: []);

		coordinator.ApplyProjectProfileSelections(ProjectPath, profile);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);
		Assert.DoesNotContain(viewModel.IgnoreOptions, option => option.Id == dynamicOptionId);

		availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);

		Assert.False(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
		Assert.False(viewModel.AllIgnoreChecked);
	}

	[Theory]
	[MemberData(nameof(DynamicOptionIds))]
	public void DynamicIgnoreNegativeMatrix_UnavailableOnlyProfile_DoesNotPromoteDynamicOptionToChecked(
		IgnoreOptionId dynamicOptionId)
	{
		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: false);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);

		var profile = new ProjectSelectionProfile(
			SelectedRootFolders: [],
			SelectedExtensions: [],
			SelectedIgnoreOptions: [IgnoreOptionId.UseGitIgnore]);

		coordinator.ApplyProjectProfileSelections(ProjectPath, profile);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);
		Assert.DoesNotContain(viewModel.IgnoreOptions, option => option.Id == dynamicOptionId);

		availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);

		Assert.False(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
		Assert.False(viewModel.AllIgnoreChecked);
		Assert.Contains(IgnoreOptionId.UseGitIgnore, coordinator.GetSelectedIgnoreOptionIds());
	}

	[Theory]
	[MemberData(nameof(DynamicOptionIds))]
	public void DynamicIgnoreNegativeMatrix_NewProjectWithoutProfile_DoesNotReusePreviousProjectUncheckedDynamicState(
		IgnoreOptionId dynamicOptionId)
	{
		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);
		coordinator.HookIgnoreListeners(viewModel.IgnoreOptions);

		coordinator.PopulateIgnoreOptionsForRootSelection([], ProjectPath);
		GetIgnoreOption(viewModel, dynamicOptionId).IsChecked = false;

		coordinator.ResetProjectProfileSelections(NextProjectPath);
		coordinator.PopulateIgnoreOptionsForRootSelection([], NextProjectPath);

		Assert.True(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
		Assert.DoesNotContain(coordinator.GetSelectedIgnoreOptionIds(), id => id == IgnoreOptionId.UseGitIgnore);
	}

	public static IEnumerable<object[]> DynamicOptionIds()
	{
		yield return [IgnoreOptionId.EmptyFolders];
		yield return [IgnoreOptionId.EmptyFiles];
		yield return [IgnoreOptionId.ExtensionlessFiles];
	}

	private static IgnoreOptionViewModel GetIgnoreOption(MainWindowViewModel viewModel, IgnoreOptionId id)
	{
		return Assert.Single(viewModel.IgnoreOptions.Where(option => option.Id == id));
	}

	private static IgnoreOptionsAvailability BuildAvailability(IgnoreOptionId dynamicOptionId, bool dynamicVisible)
	{
		return dynamicOptionId switch
		{
			IgnoreOptionId.EmptyFolders => new IgnoreOptionsAvailability(
				IncludeGitIgnore: false,
				IncludeSmartIgnore: false,
				IncludeEmptyFolders: dynamicVisible,
				EmptyFoldersCount: dynamicVisible ? 2 : 0,
				ShowAdvancedCounts: true),
			IgnoreOptionId.EmptyFiles => new IgnoreOptionsAvailability(
				IncludeGitIgnore: false,
				IncludeSmartIgnore: false,
				IncludeEmptyFiles: dynamicVisible,
				EmptyFilesCount: dynamicVisible ? 3 : 0,
				ShowAdvancedCounts: true),
			IgnoreOptionId.ExtensionlessFiles => new IgnoreOptionsAvailability(
				IncludeGitIgnore: false,
				IncludeSmartIgnore: false,
				IncludeExtensionlessFiles: dynamicVisible,
				ExtensionlessFilesCount: dynamicVisible ? 4 : 0,
				ShowAdvancedCounts: true),
			_ => throw new ArgumentOutOfRangeException(nameof(dynamicOptionId), dynamicOptionId, null)
		};
	}

	private static SelectionSyncCoordinator CreateCoordinator(
		MainWindowViewModel viewModel,
		Func<IgnoreOptionsAvailability> availabilityProvider)
	{
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
			(_, _, _) => new IgnoreRules(
				IgnoreHiddenFolders: false,
				IgnoreHiddenFiles: false,
				IgnoreDotFolders: false,
				IgnoreDotFiles: false,
				SmartIgnoredFolders: new HashSet<string>(),
				SmartIgnoredFiles: new HashSet<string>()),
			(_, _) => availabilityProvider(),
			_ => false,
			() => null);
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
				["Settings.Ignore.DotFolders"] = "dot folders",
				["Settings.Ignore.DotFiles"] = "dot files",
				["Settings.Ignore.EmptyFolders"] = "Empty folders",
				["Settings.Ignore.EmptyFiles"] = "Empty files",
				["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
			}
		};

		return new StubLocalizationCatalog(data);
	}
}
