namespace DevProjex.Application.Services;

public sealed class IgnoreOptionsService(LocalizationService localization)
{
	public IReadOnlyList<IgnoreOptionDescriptor> GetOptions(IgnoreOptionsAvailability availability)
	{
		var options = new List<IgnoreOptionDescriptor>();
		if (availability.IncludeSmartIgnore)
		{
			options.Add(new IgnoreOptionDescriptor(
				IgnoreOptionId.SmartIgnore,
				localization["Settings.Ignore.SmartIgnore"],
				true));
		}

		if (availability.IncludeGitIgnore)
		{
			options.Add(new IgnoreOptionDescriptor(
				IgnoreOptionId.UseGitIgnore,
				localization["Settings.Ignore.UseGitIgnore"],
				true));
		}

		options.AddRange([
			new IgnoreOptionDescriptor(IgnoreOptionId.HiddenFolders, localization["Settings.Ignore.HiddenFolders"], true),
			new IgnoreOptionDescriptor(IgnoreOptionId.HiddenFiles, localization["Settings.Ignore.HiddenFiles"], true),
			new IgnoreOptionDescriptor(IgnoreOptionId.DotFolders, localization["Settings.Ignore.DotFolders"], true),
			new IgnoreOptionDescriptor(IgnoreOptionId.DotFiles, localization["Settings.Ignore.DotFiles"], true)
		]);

		if (availability.IncludeExtensionlessFiles)
		{
			var extensionlessLabel = availability.ExtensionlessFilesCount > 0
				? $"{localization["Settings.Ignore.ExtensionlessFiles"]} ({availability.ExtensionlessFilesCount})"
				: localization["Settings.Ignore.ExtensionlessFiles"];

			options.Add(new IgnoreOptionDescriptor(
				IgnoreOptionId.ExtensionlessFiles,
				extensionlessLabel,
				false));
		}

		return options;
	}

	public IReadOnlyList<IgnoreOptionDescriptor> GetOptions()
	{
		return GetOptions(new IgnoreOptionsAvailability(
			IncludeGitIgnore: false,
			IncludeSmartIgnore: false));
	}

	public IReadOnlyList<IgnoreOptionDescriptor> GetOptions(bool includeGitIgnore)
	{
		return GetOptions(new IgnoreOptionsAvailability(
			IncludeGitIgnore: includeGitIgnore,
			IncludeSmartIgnore: false));
	}
}
