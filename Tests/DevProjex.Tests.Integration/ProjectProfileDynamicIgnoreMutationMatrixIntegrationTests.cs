using DevProjex.Avalonia.Coordinators;
using DevProjex.Avalonia.ViewModels;

namespace DevProjex.Tests.Integration;

public sealed class ProjectProfileDynamicIgnoreMutationMatrixIntegrationTests
{
	[Theory]
	[MemberData(nameof(MutationCases))]
	public void ProjectProfileMutationMatrix_DynamicIgnoreState_AppliesExpectedPreparedMode(
		int pathMode,
		IgnoreOptionId dynamicOptionId,
		ProfileMutationMode mutationMode)
	{
		using var temp = new TemporaryDirectory();
		var store = new ProjectProfileStore(() => temp.Path);
		var canonicalPath = Path.Combine(temp.Path, "workspace", "RepoA");
		Directory.CreateDirectory(canonicalPath);
		var savePath = BuildPathByMode(canonicalPath, pathMode);

		ApplyMutation(store, savePath, dynamicOptionId, mutationMode);

		var availability = BuildAvailability(dynamicOptionId, dynamicVisible: false);
		var viewModel = CreateViewModel();
		using var coordinator = CreateCoordinator(viewModel, () => availability);

		ApplyPreparedSelections(store, coordinator, canonicalPath);

		coordinator.PopulateIgnoreOptionsForRootSelection([], canonicalPath);
		Assert.DoesNotContain(viewModel.IgnoreOptions, option => option.Id == dynamicOptionId);

		availability = BuildAvailability(dynamicOptionId, dynamicVisible: true);
		coordinator.PopulateIgnoreOptionsForRootSelection([], canonicalPath);

		AssertExpectedPreparedState(viewModel, coordinator, dynamicOptionId, mutationMode);
	}

	public static IEnumerable<object[]> MutationCases()
	{
		var pathModes = new[] { 0, 1, 2, 3 };
		var dynamicOptionIds = new[]
		{
			IgnoreOptionId.EmptyFolders,
			IgnoreOptionId.EmptyFiles,
			IgnoreOptionId.ExtensionlessFiles
		};

		foreach (var pathMode in pathModes)
		{
			foreach (var dynamicOptionId in dynamicOptionIds)
			{
				foreach (var mutationMode in Enum.GetValues<ProfileMutationMode>())
					yield return [pathMode, dynamicOptionId, mutationMode];
			}
		}
	}

	private static void ApplyPreparedSelections(
		ProjectProfileStore store,
		SelectionSyncCoordinator coordinator,
		string canonicalPath)
	{
		if (store.TryLoadProfile(canonicalPath, out var profile))
			coordinator.ApplyProjectProfileSelections(canonicalPath, profile);
		else
			coordinator.ResetProjectProfileSelections(canonicalPath);
	}

	private static void ApplyMutation(
		ProjectProfileStore store,
		string savePath,
		IgnoreOptionId dynamicOptionId,
		ProfileMutationMode mutationMode)
	{
		switch (mutationMode)
		{
			case ProfileMutationMode.SelectedDynamicOnly:
				store.SaveProfile(savePath, CreateProfile([dynamicOptionId]));
				break;

			case ProfileMutationMode.EmptySelection:
				store.SaveProfile(savePath, CreateProfile([]));
				break;

			case ProfileMutationMode.UnavailableOnly:
				store.SaveProfile(savePath, CreateProfile([IgnoreOptionId.UseGitIgnore]));
				break;

			case ProfileMutationMode.MixedVisibleSelection:
				store.SaveProfile(savePath, CreateProfile([dynamicOptionId, IgnoreOptionId.HiddenFiles]));
				break;

			case ProfileMutationMode.ClearedAfterSave:
				store.SaveProfile(savePath, CreateProfile([dynamicOptionId]));
				store.ClearAllProfiles();
				break;

			case ProfileMutationMode.CorruptedAfterSave:
				store.SaveProfile(savePath, CreateProfile([dynamicOptionId]));
				File.WriteAllText(store.GetPath(), "{ definitely-not-valid-json");
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(mutationMode), mutationMode, null);
		}
	}

	private static void AssertExpectedPreparedState(
		MainWindowViewModel viewModel,
		SelectionSyncCoordinator coordinator,
		IgnoreOptionId dynamicOptionId,
		ProfileMutationMode mutationMode)
	{
		var visibleIds = viewModel.IgnoreOptions.Select(option => option.Id).ToHashSet();
		var selectedIds = coordinator.GetSelectedIgnoreOptionIds().ToHashSet();

		switch (mutationMode)
		{
			case ProfileMutationMode.SelectedDynamicOnly:
				Assert.True(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
				Assert.True(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFiles).IsChecked);
				Assert.True(viewModel.AllIgnoreChecked);
				AssertIgnoreSetEqual(selectedIds, visibleIds);
				break;

			case ProfileMutationMode.EmptySelection:
				Assert.False(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
				Assert.False(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked);
				Assert.False(viewModel.AllIgnoreChecked);
				Assert.Empty(selectedIds);
				break;

			case ProfileMutationMode.UnavailableOnly:
				Assert.False(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
				Assert.True(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked);
				Assert.False(viewModel.AllIgnoreChecked);
				AssertIgnoreSetEqual(
					selectedIds,
					[
						IgnoreOptionId.HiddenFolders,
						IgnoreOptionId.HiddenFiles,
						IgnoreOptionId.DotFolders,
						IgnoreOptionId.DotFiles,
						IgnoreOptionId.UseGitIgnore
					]);
				break;

			case ProfileMutationMode.ClearedAfterSave:
			case ProfileMutationMode.CorruptedAfterSave:
				Assert.True(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
				Assert.True(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked);
				Assert.True(viewModel.AllIgnoreChecked);
				AssertIgnoreSetEqual(selectedIds, visibleIds);
				break;

			case ProfileMutationMode.MixedVisibleSelection:
				Assert.True(GetIgnoreOption(viewModel, dynamicOptionId).IsChecked);
				Assert.True(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFiles).IsChecked);
				Assert.False(GetIgnoreOption(viewModel, IgnoreOptionId.HiddenFolders).IsChecked);
				Assert.False(viewModel.AllIgnoreChecked);
				AssertIgnoreSetEqual(selectedIds, [dynamicOptionId, IgnoreOptionId.HiddenFiles]);
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(mutationMode), mutationMode, null);
		}
	}

	private static IgnoreOptionViewModel GetIgnoreOption(MainWindowViewModel viewModel, IgnoreOptionId id)
	{
		return Assert.Single(viewModel.IgnoreOptions.Where(option => option.Id == id));
	}

	private static void AssertIgnoreSetEqual(
		IReadOnlyCollection<IgnoreOptionId> actual,
		IEnumerable<IgnoreOptionId> expected)
	{
		var actualSet = actual.ToHashSet();
		var expectedSet = expected.ToHashSet();
		Assert.Equal(expectedSet.Count, actualSet.Count);
		foreach (var optionId in expectedSet)
			Assert.Contains(optionId, actualSet);
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

	private static ProjectSelectionProfile CreateProfile(IReadOnlyCollection<IgnoreOptionId> selectedIgnoreOptions)
	{
		return new ProjectSelectionProfile(
			SelectedRootFolders: [],
			SelectedExtensions: [],
			SelectedIgnoreOptions: selectedIgnoreOptions);
	}

	private static SelectionSyncCoordinator CreateCoordinator(
		MainWindowViewModel viewModel,
		Func<IgnoreOptionsAvailability> availabilityProvider)
	{
		var localization = new LocalizationService(new TestLocalizationCatalog(), AppLanguage.En);
		var scanOptions = new ScanOptionsUseCase(new FileSystemScanner());
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
		var localization = new LocalizationService(new TestLocalizationCatalog(), AppLanguage.En);
		return new MainWindowViewModel(localization, new HelpContentProvider());
	}

	private static string BuildPathByMode(string canonicalPath, int mode)
	{
		return mode switch
		{
			0 => canonicalPath,
			1 => $"{canonicalPath}{Path.DirectorySeparatorChar}",
			2 => canonicalPath.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
			3 => Path.GetRelativePath(Environment.CurrentDirectory, canonicalPath),
			_ => canonicalPath
		};
	}

	private sealed class TestLocalizationCatalog : ILocalizationCatalog
	{
		private static readonly IReadOnlyDictionary<string, string> EnglishCatalog =
			new Dictionary<string, string>
			{
				["Settings.Ignore.SmartIgnore"] = "Smart ignore",
				["Settings.Ignore.UseGitIgnore"] = "Use .gitignore",
				["Settings.Ignore.HiddenFolders"] = "Hidden folders",
				["Settings.Ignore.HiddenFiles"] = "Hidden files",
				["Settings.Ignore.DotFolders"] = "Dot folders",
				["Settings.Ignore.DotFiles"] = "Dot files",
				["Settings.Ignore.EmptyFolders"] = "Empty folders",
				["Settings.Ignore.EmptyFiles"] = "Empty files",
				["Settings.Ignore.ExtensionlessFiles"] = "Files without extension"
			};

		public IReadOnlyDictionary<string, string> Get(AppLanguage language)
		{
			return EnglishCatalog;
		}
	}

	public enum ProfileMutationMode
	{
		SelectedDynamicOnly = 0,
		EmptySelection = 1,
		UnavailableOnly = 2,
		MixedVisibleSelection = 3,
		ClearedAfterSave = 4,
		CorruptedAfterSave = 5
	}
}
