using System.Runtime.CompilerServices;

namespace DevProjex.Kernel.Models;

public sealed record IgnoreRules(
	bool IgnoreHiddenFolders,
	bool IgnoreHiddenFiles,
	bool IgnoreDotFolders,
	bool IgnoreDotFiles,
	IReadOnlySet<string> SmartIgnoredFolders,
	IReadOnlySet<string> SmartIgnoredFiles)
{
	private static readonly StringComparison PathComparison = OperatingSystem.IsLinux()
		? StringComparison.Ordinal
		: StringComparison.OrdinalIgnoreCase;

	public bool UseGitIgnore { get; init; }
	public bool UseSmartIgnore { get; init; }
	public bool IgnoreEmptyFolders { get; init; }
	public bool IgnoreExtensionlessFiles { get; init; }

	public GitIgnoreMatcher GitIgnoreMatcher { get; init; } = GitIgnoreMatcher.Empty;

	public IReadOnlyList<ScopedGitIgnoreMatcher> ScopedGitIgnoreMatchers { get; init; } =
		[];

	public IReadOnlyList<string> SmartIgnoreScopeRoots { get; init; } =
		[];

	public readonly record struct GitIgnoreEvaluation(bool IsIgnored, bool ShouldTraverseIgnoredDirectory)
	{
		public static readonly GitIgnoreEvaluation NotIgnored = new(false, false);
	}

	public GitIgnoreMatcher ResolveGitIgnoreMatcher(string fullPath)
	{
		if (!UseGitIgnore)
			return GitIgnoreMatcher.Empty;

		if (ScopedGitIgnoreMatchers.Count == 0)
			return GitIgnoreMatcher;

		ScopedGitIgnoreMatcher? bestMatch = null;
		foreach (var scoped in ScopedGitIgnoreMatchers)
		{
			if (IsPathInsideScope(fullPath, scoped.ScopeRootPath))
			{
				if (bestMatch is null || scoped.ScopeRootPath.Length > bestMatch.ScopeRootPath.Length)
					bestMatch = scoped;
			}
		}

		return bestMatch?.Matcher ?? GitIgnoreMatcher.Empty;
	}

	public GitIgnoreEvaluation EvaluateGitIgnore(string fullPath, bool isDirectory, string name)
	{
		if (!UseGitIgnore)
			return GitIgnoreEvaluation.NotIgnored;

		var scopedCount = ScopedGitIgnoreMatchers.Count;
		if (scopedCount == 0)
			return EvaluateWithSingleMatcher(GitIgnoreMatcher, fullPath, isDirectory, name);

		if (scopedCount == 1)
		{
			var scoped = ScopedGitIgnoreMatchers[0];
			if (!IsPathInsideScope(fullPath, scoped.ScopeRootPath))
				return GitIgnoreEvaluation.NotIgnored;

			return EvaluateWithSingleMatcher(scoped.Matcher, fullPath, isDirectory, name);
		}

		var hasMatch = false;
		var ignored = false;
		var hasNegationAwareScope = false;

		foreach (var scoped in ScopedGitIgnoreMatchers)
		{
			if (!IsPathInsideScope(fullPath, scoped.ScopeRootPath))
				continue;

			if (isDirectory && scoped.Matcher.HasNegationRules)
				hasNegationAwareScope = true;

			var evaluation = scoped.Matcher.Evaluate(fullPath, isDirectory, name);
			if (!evaluation.HasMatch)
				continue;

			hasMatch = true;
			ignored = evaluation.IsIgnored;
		}

		if (!hasMatch || !ignored)
			return GitIgnoreEvaluation.NotIgnored;

		if (!isDirectory || !hasNegationAwareScope)
			return new GitIgnoreEvaluation(IsIgnored: true, ShouldTraverseIgnoredDirectory: false);

		foreach (var scoped in ScopedGitIgnoreMatchers)
		{
			if (!IsPathInsideScope(fullPath, scoped.ScopeRootPath))
				continue;

			if (scoped.Matcher.ShouldTraverseIgnoredDirectory(fullPath, name))
				return new GitIgnoreEvaluation(IsIgnored: true, ShouldTraverseIgnoredDirectory: true);
		}

		return new GitIgnoreEvaluation(IsIgnored: true, ShouldTraverseIgnoredDirectory: false);
	}

	public bool IsGitIgnored(string fullPath, bool isDirectory, string name)
	{
		return EvaluateGitIgnore(fullPath, isDirectory, name).IsIgnored;
	}

	public bool ShouldTraverseGitIgnoredDirectory(string fullPath, string name)
	{
		return EvaluateGitIgnore(fullPath, isDirectory: true, name).ShouldTraverseIgnoredDirectory;
	}

	public bool ShouldApplySmartIgnore(string fullPath)
	{
		if (!UseSmartIgnore)
			return false;

		if (SmartIgnoreScopeRoots.Count == 0)
			return true;

		foreach (var scopeRoot in SmartIgnoreScopeRoots)
		{
			if (IsPathInsideScope(fullPath, scopeRoot))
				return true;
		}

		return false;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsPathInsideScope(string fullPath, string scopeRootPath)
	{
		if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(scopeRootPath))
			return false;

		// Use Span for faster comparison
		var fullSpan = fullPath.AsSpan();
		var scopeSpan = scopeRootPath.AsSpan();

		if (!fullSpan.StartsWith(scopeSpan, PathComparison))
			return false;

		if (fullSpan.Length == scopeSpan.Length)
			return true;

		var next = fullSpan[scopeSpan.Length];
		return next == Path.DirectorySeparatorChar || next == Path.AltDirectorySeparatorChar;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static GitIgnoreEvaluation EvaluateWithSingleMatcher(
		GitIgnoreMatcher matcher,
		string fullPath,
		bool isDirectory,
		string name)
	{
		var evaluation = matcher.Evaluate(fullPath, isDirectory, name);
		if (!evaluation.HasMatch || !evaluation.IsIgnored)
			return GitIgnoreEvaluation.NotIgnored;

		if (!isDirectory)
			return new GitIgnoreEvaluation(IsIgnored: true, ShouldTraverseIgnoredDirectory: false);

		return new GitIgnoreEvaluation(
			IsIgnored: true,
			ShouldTraverseIgnoredDirectory: matcher.ShouldTraverseIgnoredDirectory(fullPath, name));
	}
}

public sealed record ScopedGitIgnoreMatcher(
	string ScopeRootPath,
	GitIgnoreMatcher Matcher);
