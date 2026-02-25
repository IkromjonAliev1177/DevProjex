namespace DevProjex.Kernel.Abstractions;

public interface IFileSystemScannerAdvanced
{
	ScanResult<ExtensionsScanData> GetExtensionsWithIgnoreOptionCounts(
		string rootPath,
		IgnoreRules rules,
		CancellationToken cancellationToken = default);

	ScanResult<ExtensionsScanData> GetRootFileExtensionsWithIgnoreOptionCounts(
		string rootPath,
		IgnoreRules rules,
		CancellationToken cancellationToken = default);
}
