namespace DevProjex.Kernel.Models;

public readonly record struct IgnoreOptionCounts(
	int HiddenFolders = 0,
	int HiddenFiles = 0,
	int DotFolders = 0,
	int DotFiles = 0,
	int EmptyFolders = 0,
	int ExtensionlessFiles = 0,
	int EmptyFiles = 0)
{
	public static readonly IgnoreOptionCounts Empty = new(
		HiddenFolders: 0,
		HiddenFiles: 0,
		DotFolders: 0,
		DotFiles: 0,
		EmptyFolders: 0,
		ExtensionlessFiles: 0,
		EmptyFiles: 0);

	public IgnoreOptionCounts Add(in IgnoreOptionCounts other)
	{
		return new IgnoreOptionCounts(
			HiddenFolders + other.HiddenFolders,
			HiddenFiles + other.HiddenFiles,
			DotFolders + other.DotFolders,
			DotFiles + other.DotFiles,
			EmptyFolders + other.EmptyFolders,
			ExtensionlessFiles + other.ExtensionlessFiles,
			EmptyFiles + other.EmptyFiles);
	}
}
