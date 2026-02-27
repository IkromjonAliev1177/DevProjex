namespace DevProjex.Tests.Unit;

public sealed class IgnoreOptionCountsExtensionlessTests
{
	[Fact]
	public void Empty_ExtensionlessFiles_IsZero()
	{
		Assert.Equal(0, IgnoreOptionCounts.Empty.ExtensionlessFiles);
	}

	[Fact]
	public void Add_ExtensionlessFiles_IsSummed()
	{
		var left = new IgnoreOptionCounts(
			HiddenFolders: 1,
			HiddenFiles: 2,
			DotFolders: 3,
			DotFiles: 4,
			EmptyFolders: 5,
			ExtensionlessFiles: 6);

		var right = new IgnoreOptionCounts(
			HiddenFolders: 10,
			HiddenFiles: 20,
			DotFolders: 30,
			DotFiles: 40,
			EmptyFolders: 50,
			ExtensionlessFiles: 60);

		var result = left.Add(right);

		Assert.Equal(11, result.HiddenFolders);
		Assert.Equal(22, result.HiddenFiles);
		Assert.Equal(33, result.DotFolders);
		Assert.Equal(44, result.DotFiles);
		Assert.Equal(55, result.EmptyFolders);
		Assert.Equal(66, result.ExtensionlessFiles);
	}
}
