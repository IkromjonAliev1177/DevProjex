namespace DevProjex.Tests.Unit;

public sealed class IgnoreRulesSmartScopeBoundaryMatrixTests
{
	public static IEnumerable<object[]> ScopeBoundaryCases()
	{
		var root = Path.Combine("C:", "repo", "app");
		var inside = Path.Combine(root, "src", "Program.cs");
		var same = root;
		var siblingPrefixTrap = Path.Combine("C:", "repo", "application", "file.cs");
		var outside = Path.Combine("C:", "repo", "lib", "file.cs");

		yield return [inside, true];
		yield return [same, true];
		yield return [siblingPrefixTrap, false];
		yield return [outside, false];

		var upper = inside.ToUpperInvariant();
		var lower = inside.ToLowerInvariant();
		var expectedCaseMatch = !OperatingSystem.IsLinux();
		yield return [upper, expectedCaseMatch];
		yield return [lower, expectedCaseMatch];

		// Alternate separator style is not normalized by IgnoreRules path checks.
		var alt = inside.Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		yield return [alt, false];
	}

	[Theory]
	[MemberData(nameof(ScopeBoundaryCases))]
	public void ShouldApplySmartIgnore_BoundaryAndCaseMatrix_IsCorrect(string candidatePath, bool expected)
	{
		var scopeRoot = Path.Combine("C:", "repo", "app");
		var rules = new IgnoreRules(
			IgnoreHiddenFolders: false,
			IgnoreHiddenFiles: false,
			IgnoreDotFolders: false,
			IgnoreDotFiles: false,
			SmartIgnoredFolders: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
			SmartIgnoredFiles: new HashSet<string>(StringComparer.OrdinalIgnoreCase))
		{
			UseSmartIgnore = true,
			SmartIgnoreScopeRoots = [scopeRoot]
		};

		Assert.Equal(expected, rules.ShouldApplySmartIgnore(candidatePath));
	}

	[Fact]
	public void ShouldApplySmartIgnore_WhenDisabled_IsAlwaysFalse()
	{
		var rules = new IgnoreRules(
			IgnoreHiddenFolders: false,
			IgnoreHiddenFiles: false,
			IgnoreDotFolders: false,
			IgnoreDotFiles: false,
			SmartIgnoredFolders: new HashSet<string>(),
			SmartIgnoredFiles: new HashSet<string>())
		{
			UseSmartIgnore = false,
			SmartIgnoreScopeRoots = [Path.Combine("C:", "repo", "app")]
		};

		Assert.False(rules.ShouldApplySmartIgnore(Path.Combine("C:", "repo", "app", "src", "a.cs")));
		Assert.False(rules.ShouldApplySmartIgnore(Path.Combine("C:", "repo", "other", "a.cs")));
	}

	[Fact]
	public void ShouldApplySmartIgnore_WithoutScopes_AllowsAnyPath()
	{
		var rules = new IgnoreRules(
			IgnoreHiddenFolders: false,
			IgnoreHiddenFiles: false,
			IgnoreDotFolders: false,
			IgnoreDotFiles: false,
			SmartIgnoredFolders: new HashSet<string>(),
			SmartIgnoredFiles: new HashSet<string>())
		{
			UseSmartIgnore = true,
			SmartIgnoreScopeRoots = []
		};

		Assert.True(rules.ShouldApplySmartIgnore("/any/path/file.txt"));
	}
}
