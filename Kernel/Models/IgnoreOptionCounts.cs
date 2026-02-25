namespace DevProjex.Kernel.Models;

public readonly record struct IgnoreOptionCounts(
	int HiddenFolders,
	int HiddenFiles,
	int DotFolders,
	int DotFiles)
{
	public static readonly IgnoreOptionCounts Empty = new(
		HiddenFolders: 0,
		HiddenFiles: 0,
		DotFolders: 0,
		DotFiles: 0);

	public IgnoreOptionCounts Add(in IgnoreOptionCounts other)
	{
		return new IgnoreOptionCounts(
			HiddenFolders + other.HiddenFolders,
			HiddenFiles + other.HiddenFiles,
			DotFolders + other.DotFolders,
			DotFiles + other.DotFiles);
	}
}
