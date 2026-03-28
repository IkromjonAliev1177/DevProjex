namespace DevProjex.Kernel;

public interface IFileSystemScannerVisibleNodeCounter
{
	ScanResult<int> GetAffectedIgnoreOptionTreeNodeCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken = default);

	ScanResult<int> GetAffectedIgnoreOptionRootFileCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken = default);
}
