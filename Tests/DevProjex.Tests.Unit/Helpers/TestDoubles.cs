namespace DevProjex.Tests.Unit.Helpers;

internal sealed class StubLocalizationCatalog(
	IReadOnlyDictionary<AppLanguage, IReadOnlyDictionary<string, string>> data)
	: ILocalizationCatalog
{
	public IReadOnlyDictionary<string, string> Get(AppLanguage language)
	{
		return data.TryGetValue(language, out var dict) ? dict : data[AppLanguage.En];
	}
}

internal sealed class StubFileSystemScanner : IFileSystemScanner
{
	public Func<string, IgnoreRules, ScanResult<HashSet<string>>> GetExtensionsHandler { get; set; } =
		(_, _) => new ScanResult<HashSet<string>>([], false, false);

	public Func<string, IgnoreRules, ScanResult<HashSet<string>>> GetRootFileExtensionsHandler { get; set; } =
		(_, _) => new ScanResult<HashSet<string>>([], false, false);

	public Func<string, IgnoreRules, ScanResult<List<string>>> GetRootFolderNamesHandler { get; set; } =
		(_, _) => new ScanResult<List<string>>([], false, false);

	public Func<string, bool> CanReadRootHandler { get; set; } = _ => true;

	public bool CanReadRoot(string rootPath) => CanReadRootHandler(rootPath);

	public ScanResult<HashSet<string>> GetExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default) =>
		GetExtensionsHandler(rootPath, rules);

	public ScanResult<HashSet<string>> GetRootFileExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default) =>
		GetRootFileExtensionsHandler(rootPath, rules);

	public ScanResult<List<string>> GetRootFolderNames(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default) =>
		GetRootFolderNamesHandler(rootPath, rules);
}

internal sealed class StubTreeBuilder : ITreeBuilder
{
	public TreeBuildResult Result { get; set; } = new(
		new FileSystemNode("root", "root", true, false, new List<FileSystemNode>()),
		false,
		false);

	public TreeBuildResult Build(string rootPath, TreeFilterOptions options, CancellationToken cancellationToken = default) => Result;
}

internal sealed class StubIconMapper : IIconMapper
{
	public string IconKey { get; set; } = "icon";
	public string GetIconKey(FileSystemNode node) => IconKey;
}

internal sealed class StubSmartIgnoreRule(SmartIgnoreResult result) : ISmartIgnoreRule
{
	public SmartIgnoreResult Evaluate(string rootPath) => result;
}
