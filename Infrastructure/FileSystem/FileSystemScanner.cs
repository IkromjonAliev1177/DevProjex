namespace DevProjex.Infrastructure.FileSystem;

public sealed class FileSystemScanner : IFileSystemScanner, IFileSystemScannerAdvanced, IFileSystemScannerEffectiveEmptyFolderCounter, IFileSystemScannerVisibleNodeCounter
{
	// Optimal parallelism for modern multi-core CPUs with NVMe SSDs
	private static readonly int MaxParallelism = Math.Max(4, Environment.ProcessorCount);
	private const int SequentialDirectoryScanThreshold = 24;

	public bool CanReadRoot(string rootPath)
	{
		try
		{
			_ = Directory.EnumerateFileSystemEntries(rootPath).GetEnumerator().MoveNext();
			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
		catch
		{
			return true;
		}
	}

	public ScanResult<HashSet<string>> GetExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
	{
		var scan = ScanExtensionsCore(
			rootPath,
			rules,
			collectIgnoreOptionCounts: false,
			includeRootDirectoryInCounts: false,
			cancellationToken);
		return new ScanResult<HashSet<string>>(scan.Value.Extensions, scan.RootAccessDenied, scan.HadAccessDenied);
	}

	public ScanResult<ExtensionsScanData> GetExtensionsWithIgnoreOptionCounts(
		string rootPath,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanExtensionsCore(
			rootPath,
			rules,
			collectIgnoreOptionCounts: true,
			includeRootDirectoryInCounts: true,
			cancellationToken);
	}

	public ScanResult<HashSet<string>> GetRootFileExtensions(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
	{
		var scan = ScanRootFilesCore(rootPath, rules, collectIgnoreOptionCounts: false, cancellationToken);
		return new ScanResult<HashSet<string>>(scan.Value.Extensions, scan.RootAccessDenied, scan.HadAccessDenied);
	}

	public ScanResult<ExtensionsScanData> GetRootFileExtensionsWithIgnoreOptionCounts(
		string rootPath,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanRootFilesCore(rootPath, rules, collectIgnoreOptionCounts: true, cancellationToken);
	}

	public ScanResult<int> GetEffectiveEmptyFolderCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanEffectiveEmptyFolderCountCore(rootPath, allowedExtensions, rules, cancellationToken);
	}

	public ScanResult<int> GetVisibleTreeNodeCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanVisibleTreeNodeCountCore(rootPath, allowedExtensions, rules, cancellationToken);
	}

	public ScanResult<int> GetVisibleRootFileCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanVisibleRootFileCountCore(rootPath, allowedExtensions, rules, cancellationToken);
	}

	public ScanResult<int> GetAffectedIgnoreOptionTreeNodeCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken = default)
	{
		return ScanAffectedIgnoreOptionTreeNodeCountCore(rootPath, allowedExtensions, rules, optionId, cancellationToken);
	}

	public ScanResult<int> GetAffectedIgnoreOptionRootFileCount(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken = default)
	{
		return ScanAffectedIgnoreOptionRootFileCountCore(rootPath, allowedExtensions, rules, optionId, cancellationToken);
	}

	public ScanResult<List<string>> GetRootFolderNames(string rootPath, IgnoreRules rules, CancellationToken cancellationToken = default)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var names = new List<string>();
		var useGitIgnore = rules.UseGitIgnore;

		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<List<string>>(names, false, false);

		try
		{
			foreach (var dir in Directory.EnumerateDirectories(rootPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var dirName = Path.GetFileName(dir);
				var directoryGitIgnore = useGitIgnore
					? rules.EvaluateGitIgnore(dir, isDirectory: true, dirName)
					: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
				if (ShouldSkipDirectoryByName(dirName, dir, rules, directoryGitIgnore))
					continue;

				names.Add(dirName);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			return new ScanResult<List<string>>(names, true, true);
		}
		catch
		{
			return new ScanResult<List<string>>(names, false, false);
		}

		names.Sort(StringComparer.OrdinalIgnoreCase);
		return new ScanResult<List<string>>(names, false, false);
	}

	/// <summary>
	/// Optimized version that avoids DirectoryInfo allocation when possible.
	/// Only creates DirectoryInfo when checking Hidden attribute.
	/// </summary>
	private static bool ShouldSkipDirectoryByName(
		string name,
		string fullPath,
		IgnoreRules rules,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (gitIgnoreEvaluation.IsIgnored)
		{
			if (!gitIgnoreEvaluation.ShouldTraverseIgnoredDirectory)
				return true;
		}

		if (rules.ShouldApplySmartIgnore(fullPath, isDirectory: true) && rules.SmartIgnoredFolders.Contains(name))
			return true;

		if (rules.IgnoreDotFolders && name.StartsWith(".", StringComparison.Ordinal))
			return true;

		if (rules.IgnoreHiddenFolders)
		{
			try
			{
				if (File.GetAttributes(fullPath).HasFlag(FileAttributes.Hidden))
					return true;
			}
			catch (IOException)
			{
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Optimized version that avoids FileInfo allocation when possible.
	/// Only checks attributes when necessary.
	/// </summary>
	private static bool ShouldSkipFileByName(
		string name,
		string fullPath,
		IgnoreRules rules,
		bool shouldApplySmartIgnore,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (gitIgnoreEvaluation.IsIgnored)
			return true;

		if (shouldApplySmartIgnore && rules.SmartIgnoredFiles.Contains(name))
			return true;

		if (rules.IgnoreDotFiles && name.StartsWith(".", StringComparison.Ordinal))
			return true;

		if (rules.IgnoreExtensionlessFiles && IsExtensionlessFileName(name))
			return true;

		if (rules.IgnoreEmptyFiles && IsZeroLengthFile(fullPath))
			return true;

		if (rules.IgnoreHiddenFiles)
		{
			try
			{
				if (File.GetAttributes(fullPath).HasFlag(FileAttributes.Hidden))
					return true;
			}
			catch (IOException)
			{
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return true;
			}
		}

		return false;
	}

	private static IgnoreRules ToggleIgnoreRule(IgnoreRules rules, IgnoreOptionId optionId)
	{
		return optionId switch
		{
			IgnoreOptionId.HiddenFolders => rules with { IgnoreHiddenFolders = !rules.IgnoreHiddenFolders },
			IgnoreOptionId.HiddenFiles => rules with { IgnoreHiddenFiles = !rules.IgnoreHiddenFiles },
			IgnoreOptionId.DotFolders => rules with { IgnoreDotFolders = !rules.IgnoreDotFolders },
			IgnoreOptionId.DotFiles => rules with { IgnoreDotFiles = !rules.IgnoreDotFiles },
			IgnoreOptionId.EmptyFolders => rules with { IgnoreEmptyFolders = !rules.IgnoreEmptyFolders },
			IgnoreOptionId.EmptyFiles => rules with { IgnoreEmptyFiles = !rules.IgnoreEmptyFiles },
			IgnoreOptionId.ExtensionlessFiles => rules with { IgnoreExtensionlessFiles = !rules.IgnoreExtensionlessFiles },
			_ => rules
		};
	}

	private static bool IsFileTargetOption(IgnoreOptionId optionId)
	{
		return optionId is IgnoreOptionId.HiddenFiles or
		       IgnoreOptionId.DotFiles or
		       IgnoreOptionId.EmptyFiles or
		       IgnoreOptionId.ExtensionlessFiles;
	}

	private static bool IsDirectoryTargetOption(IgnoreOptionId optionId)
	{
		return optionId is IgnoreOptionId.HiddenFolders or IgnoreOptionId.DotFolders;
	}

	private static bool MatchesFileTargetOption(string name, string fullPath, IgnoreOptionId optionId)
	{
		return optionId switch
		{
			IgnoreOptionId.HiddenFiles => HasHiddenAttribute(fullPath),
			IgnoreOptionId.DotFiles => IsDotName(name),
			IgnoreOptionId.EmptyFiles => IsZeroLengthFile(fullPath),
			IgnoreOptionId.ExtensionlessFiles => IsExtensionlessFileName(name),
			_ => false
		};
	}

	private static bool MatchesDirectoryTargetOption(string name, string fullPath, IgnoreOptionId optionId)
	{
		return optionId switch
		{
			IgnoreOptionId.HiddenFolders => HasHiddenAttribute(fullPath),
			IgnoreOptionId.DotFolders => IsDotName(name),
			_ => false
		};
	}

	private static DirectoryToggleRuleState EvaluateDirectoryToggleRuleState(
		string name,
		string fullPath,
		IgnoreRules rules,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (gitIgnoreEvaluation.IsIgnored && !gitIgnoreEvaluation.ShouldTraverseIgnoredDirectory)
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (rules.ShouldApplySmartIgnore(fullPath, isDirectory: true) && rules.SmartIgnoredFolders.Contains(name))
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (rules.IgnoreDotFolders && IsDotName(name))
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (rules.IgnoreHiddenFolders && HasHiddenAttribute(fullPath))
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		return new DirectoryToggleRuleState(
			CanTraverseChildren: true,
			IsSelfIgnoredButTraversed: gitIgnoreEvaluation.IsIgnored && gitIgnoreEvaluation.ShouldTraverseIgnoredDirectory);
	}

	private static bool IsDirectoryLocallyVisible(
		DirectoryToggleRuleState ruleState,
		bool ignoreEmptyFolders,
		bool hasVisibleContent)
	{
		if (!ruleState.CanTraverseChildren)
			return false;

		if (!hasVisibleContent)
		{
			if (ruleState.IsSelfIgnoredButTraversed)
				return false;
			if (ignoreEmptyFolders)
				return false;
		}

		return true;
	}

	private ScanResult<ExtensionsScanData> ScanExtensionsCore(
		string rootPath,
		IgnoreRules rules,
		bool collectIgnoreOptionCounts,
		bool includeRootDirectoryInCounts,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var uniqueExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var useGitIgnore = rules.UseGitIgnore;
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
		{
			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(uniqueExtensions, IgnoreOptionCounts.Empty),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		var normalizedRootPath = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var rootName = Path.GetFileName(normalizedRootPath);
		var rootGitIgnore = useGitIgnore
			? rules.EvaluateGitIgnore(rootPath, isDirectory: true, rootName)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;

		// Selected root folders must obey the same directory-level rules as the tree itself.
		// Otherwise a stale root selection (for example a dot-folder discovered before the
		// dynamic DotFolders toggle appeared) would still leak its subtree into counts.
		if (ShouldSkipDirectoryByName(rootName, rootPath, rules, rootGitIgnore))
		{
			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(uniqueExtensions, IgnoreOptionCounts.Empty),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		// Directory discovery is single-threaded; keep it allocation-light and parent-indexed.
		var directories = new List<DirectoryScanNode>(capacity: 256);
		var rootAccessDenied = 0;
		var hadAccessDenied = 0;
		var directoryCounts = default(MutableIgnoreOptionCounts);

		// First pass: collect all traversable directories and parent links.
		var pending = new Stack<(string Path, int ParentIndex, bool IsRootDirectory)>();
		pending.Push((rootPath, ParentIndex: -1, IsRootDirectory: true));

		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (dir, parentIndex, isRootDirectory) = pending.Pop();
			var currentDirectoryIndex = directories.Count;
			directories.Add(new DirectoryScanNode(dir, parentIndex, isAccessDenied: false));

			if (collectIgnoreOptionCounts && isRootDirectory && includeRootDirectoryInCounts)
				AccumulateDirectoryIgnoreOptionCounts(dir, Path.GetFileName(dir), ref directoryCounts);

			try
			{
				foreach (var sd in Directory.EnumerateDirectories(dir))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var dirName = Path.GetFileName(sd);
					if (collectIgnoreOptionCounts)
						AccumulateDirectoryIgnoreOptionCounts(sd, dirName, ref directoryCounts);

					var directoryGitIgnore = useGitIgnore
						? rules.EvaluateGitIgnore(sd, isDirectory: true, dirName)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					if (ShouldSkipDirectoryByName(dirName, sd, rules, directoryGitIgnore))
						continue;

					pending.Push((sd, currentDirectoryIndex, IsRootDirectory: false));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				var accessDeniedNode = directories[currentDirectoryIndex];
				accessDeniedNode.IsAccessDenied = true;
				directories[currentDirectoryIndex] = accessDeniedNode;
				Interlocked.Exchange(ref hadAccessDenied, 1);
				if (isRootDirectory) Interlocked.Exchange(ref rootAccessDenied, 1);
				continue;
			}
			catch
			{
				continue;
			}
		}

		var mergeLock = new object();
		var fileCounts = default(MutableIgnoreOptionCounts);
		var hasVisibleFilesByDirectory = new bool[directories.Count];
		var isAccessDeniedByDirectory = new bool[directories.Count];
		for (var i = 0; i < directories.Count; i++)
			isAccessDeniedByDirectory[i] = directories[i].IsAccessDenied;

		if (directories.Count < SequentialDirectoryScanThreshold)
		{
			for (var index = 0; index < directories.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();
				var dir = directories[index].Path;
				var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);

				var localCounts = default(MutableIgnoreOptionCounts);
				var hasVisibleFiles = false;
				try
				{
					foreach (var file in Directory.EnumerateFiles(dir))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var name = Path.GetFileName(file);
						if (collectIgnoreOptionCounts)
							AccumulateFileIgnoreOptionCounts(file, name, ref localCounts);

						var fileGitIgnore = useGitIgnore
							? rules.EvaluateGitIgnore(file, isDirectory: false, name)
							: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
						if (ShouldSkipFileByName(name, file, rules, shouldApplySmartIgnoreForFiles, fileGitIgnore))
							continue;

						hasVisibleFiles = true;
						var ext = Path.GetExtension(name);
						if (IsExtensionlessFileName(name))
							uniqueExtensions.Add(name);
						else if (!string.IsNullOrWhiteSpace(ext))
							uniqueExtensions.Add(ext);
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (UnauthorizedAccessException)
				{
					Interlocked.Exchange(ref hadAccessDenied, 1);
					isAccessDeniedByDirectory[index] = true;
					continue;
				}
				catch
				{
					continue;
				}

				hasVisibleFilesByDirectory[index] = hasVisibleFiles;
				if (collectIgnoreOptionCounts)
					fileCounts.Add(localCounts);
			}
		}
		else
		{
			var parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Min(MaxParallelism, directories.Count),
				CancellationToken = cancellationToken
			};

			Parallel.For(
				0,
				directories.Count,
				parallelOptions,
				() => new LocalExtensionScanState(),
				(index, _, localState) =>
				{
					parallelOptions.CancellationToken.ThrowIfCancellationRequested();
					var dir = directories[index].Path;
					var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);

					var hasVisibleFiles = false;
					try
					{
						foreach (var file in Directory.EnumerateFiles(dir))
						{
							parallelOptions.CancellationToken.ThrowIfCancellationRequested();

							var name = Path.GetFileName(file);
							if (collectIgnoreOptionCounts)
								AccumulateFileIgnoreOptionCounts(file, name, ref localState.Counts);

							var fileGitIgnore = useGitIgnore
								? rules.EvaluateGitIgnore(file, isDirectory: false, name)
								: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
							if (ShouldSkipFileByName(name, file, rules, shouldApplySmartIgnoreForFiles, fileGitIgnore))
								continue;

							hasVisibleFiles = true;
							var ext = Path.GetExtension(name);
							if (IsExtensionlessFileName(name))
								localState.Extensions.Add(name);
							else if (!string.IsNullOrWhiteSpace(ext))
								localState.Extensions.Add(ext);
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (UnauthorizedAccessException)
					{
						Interlocked.Exchange(ref hadAccessDenied, 1);
						isAccessDeniedByDirectory[index] = true;
						return localState;
					}
					catch
					{
						return localState;
					}

					hasVisibleFilesByDirectory[index] = hasVisibleFiles;
					return localState;
				},
				localState =>
				{
					if (localState.Extensions.Count == 0 && !collectIgnoreOptionCounts)
						return;

					lock (mergeLock)
					{
						if (localState.Extensions.Count > 0)
							uniqueExtensions.UnionWith(localState.Extensions);
						if (collectIgnoreOptionCounts)
							fileCounts.Add(localState.Counts);
					}
				});
		}

		var emptyFolderCount = 0;
		if (collectIgnoreOptionCounts && directories.Count > 0)
		{
			// Bottom-up fold simulates IgnoreEmptyFolders pruning without a second filesystem pass.
			var nonPrunedChildCounts = new int[directories.Count];
			for (var index = directories.Count - 1; index >= 0; index--)
			{
				var hasVisibleFiles = hasVisibleFilesByDirectory[index];
				var hasVisibleChildren = nonPrunedChildCounts[index] > 0;
				var isAccessDenied = isAccessDeniedByDirectory[index];
				var parentIndex = directories[index].ParentIndex;

				var shouldRemain = isAccessDenied || hasVisibleFiles || hasVisibleChildren;
				if (!shouldRemain)
				{
					if (parentIndex >= 0 || includeRootDirectoryInCounts)
						emptyFolderCount++;
					continue;
				}

				if (parentIndex >= 0)
					nonPrunedChildCounts[parentIndex]++;
			}
		}

		var counts = collectIgnoreOptionCounts
			? directoryCounts.ToImmutable().Add(fileCounts.ToImmutable()) with { EmptyFolders = emptyFolderCount }
			: IgnoreOptionCounts.Empty;

		return new ScanResult<ExtensionsScanData>(
			new ExtensionsScanData(uniqueExtensions, counts),
			rootAccessDenied == 1,
			hadAccessDenied == 1);
	}

	private ScanResult<ExtensionsScanData> ScanRootFilesCore(
		string rootPath,
		IgnoreRules rules,
		bool collectIgnoreOptionCounts,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var useGitIgnore = rules.UseGitIgnore;
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
		{
			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(exts, IgnoreOptionCounts.Empty),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		var counts = default(MutableIgnoreOptionCounts);
		var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(rootPath, isDirectory: true);
		try
		{
			foreach (var file in Directory.EnumerateFiles(rootPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = Path.GetFileName(file);
				if (collectIgnoreOptionCounts)
					AccumulateFileIgnoreOptionCounts(file, name, ref counts);

				var fileGitIgnore = useGitIgnore
					? rules.EvaluateGitIgnore(file, isDirectory: false, name)
					: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
				if (ShouldSkipFileByName(name, file, rules, shouldApplySmartIgnoreForFiles, fileGitIgnore))
					continue;

				var ext = Path.GetExtension(name);
				if (IsExtensionlessFileName(name))
					exts.Add(name);
				else if (!string.IsNullOrWhiteSpace(ext))
					exts.Add(ext);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(exts, IgnoreOptionCounts.Empty),
				RootAccessDenied: true,
				HadAccessDenied: true);
		}
		catch
		{
			return new ScanResult<ExtensionsScanData>(
				new ExtensionsScanData(exts, IgnoreOptionCounts.Empty),
				RootAccessDenied: false,
				HadAccessDenied: false);
		}

		return new ScanResult<ExtensionsScanData>(
			new ExtensionsScanData(exts, collectIgnoreOptionCounts ? counts.ToImmutable() : IgnoreOptionCounts.Empty),
			RootAccessDenied: false,
			HadAccessDenied: false);
	}

	private ScanResult<int> ScanVisibleRootFileCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var useGitIgnore = rules.UseGitIgnore;
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var visibleFileCount = 0;
		var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(rootPath, isDirectory: true);
		try
		{
			foreach (var file in Directory.EnumerateFiles(rootPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = Path.GetFileName(file);
				var fileGitIgnore = useGitIgnore
					? rules.EvaluateGitIgnore(file, isDirectory: false, name)
					: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
				if (!ShouldTreatFileAsVisibleForTree(
					    name,
					    file,
					    allowedExtensions,
					    rules,
					    shouldApplySmartIgnoreForFiles,
					    fileGitIgnore))
				{
					continue;
				}

				visibleFileCount++;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			return new ScanResult<int>(0, RootAccessDenied: true, HadAccessDenied: true);
		}
		catch
		{
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);
		}

		return new ScanResult<int>(visibleFileCount, RootAccessDenied: false, HadAccessDenied: false);
	}

	private ScanResult<int> ScanAffectedIgnoreOptionRootFileCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!IsFileTargetOption(optionId) || string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var toggledRules = ToggleIgnoreRule(rules, optionId);
		var useGitIgnore = rules.UseGitIgnore;
		var toggledUseGitIgnore = toggledRules.UseGitIgnore;
		var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(rootPath, isDirectory: true);
		var toggledShouldApplySmartIgnoreForFiles = toggledRules.ShouldApplySmartIgnore(rootPath, isDirectory: true);
		var affectedFileCount = 0;

		try
		{
			foreach (var file in Directory.EnumerateFiles(rootPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = Path.GetFileName(file);
				var fileGitIgnore = useGitIgnore
					? rules.EvaluateGitIgnore(file, isDirectory: false, name)
					: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
				var toggledFileGitIgnore = toggledUseGitIgnore
					? toggledRules.EvaluateGitIgnore(file, isDirectory: false, name)
					: IgnoreRules.GitIgnoreEvaluation.NotIgnored;

				var baseVisible = ShouldTreatFileAsVisibleForTree(
					name,
					file,
					allowedExtensions,
					rules,
					shouldApplySmartIgnoreForFiles,
					fileGitIgnore);
				var toggledVisible = ShouldTreatFileAsVisibleForTree(
					name,
					file,
					allowedExtensions,
					toggledRules,
					toggledShouldApplySmartIgnoreForFiles,
					toggledFileGitIgnore);

				if (baseVisible == toggledVisible)
					continue;
				if (!MatchesFileTargetOption(name, file, optionId))
					continue;

				affectedFileCount++;
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			return new ScanResult<int>(0, RootAccessDenied: true, HadAccessDenied: true);
		}
		catch
		{
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);
		}

		return new ScanResult<int>(affectedFileCount, RootAccessDenied: false, HadAccessDenied: false);
	}

	private ScanResult<int> ScanAffectedIgnoreOptionTreeNodeCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		IgnoreOptionId optionId,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (!IsDirectoryTargetOption(optionId) && !IsFileTargetOption(optionId))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var toggledRules = ToggleIgnoreRule(rules, optionId);
		var useGitIgnore = rules.UseGitIgnore;
		var toggledUseGitIgnore = toggledRules.UseGitIgnore;
		var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var rootGitIgnore = useGitIgnore
			? rules.EvaluateGitIgnore(rootPath, isDirectory: true, rootName)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
		var toggledRootGitIgnore = toggledUseGitIgnore
			? toggledRules.EvaluateGitIgnore(rootPath, isDirectory: true, rootName)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
		var rootBaseRuleState = EvaluateDirectoryToggleRuleState(rootName, rootPath, rules, rootGitIgnore);
		var rootToggledRuleState = EvaluateDirectoryToggleRuleState(rootName, rootPath, toggledRules, toggledRootGitIgnore);

		if (!rootBaseRuleState.CanTraverseChildren && !rootToggledRuleState.CanTraverseChildren)
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var directories = new List<ToggleVisibilityScanNode>(capacity: 256);
		var rootAccessDenied = 0;
		var hadAccessDenied = 0;
		var pending = new Stack<(string Path, int ParentIndex, bool IsRootDirectory, DirectoryToggleRuleState BaseRuleState, DirectoryToggleRuleState ToggledRuleState)>();
		pending.Push((rootPath, ParentIndex: -1, IsRootDirectory: true, rootBaseRuleState, rootToggledRuleState));

		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (dir, parentIndex, isRootDirectory, baseRuleState, toggledRuleState) = pending.Pop();
			var currentDirectoryIndex = directories.Count;
			directories.Add(new ToggleVisibilityScanNode(
				dir,
				parentIndex,
				baseIsAccessDenied: false,
				toggledIsAccessDenied: false,
				baseRuleState,
				toggledRuleState));

			if (!baseRuleState.CanTraverseChildren && !toggledRuleState.CanTraverseChildren)
				continue;

			try
			{
				foreach (var childDirectory in Directory.EnumerateDirectories(dir))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var childName = Path.GetFileName(childDirectory);
					var childGitIgnore = useGitIgnore
						? rules.EvaluateGitIgnore(childDirectory, isDirectory: true, childName)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					var toggledChildGitIgnore = toggledUseGitIgnore
						? toggledRules.EvaluateGitIgnore(childDirectory, isDirectory: true, childName)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					var childBaseRuleState = EvaluateDirectoryToggleRuleState(childName, childDirectory, rules, childGitIgnore);
					var childToggledRuleState = EvaluateDirectoryToggleRuleState(childName, childDirectory, toggledRules, toggledChildGitIgnore);

					if (!childBaseRuleState.CanTraverseChildren && !childToggledRuleState.CanTraverseChildren)
					{
						// The subtree is unreachable in both states, so it cannot
						// contribute any direct count for this toggle.
						continue;
					}

					pending.Push((
						childDirectory,
						currentDirectoryIndex,
						IsRootDirectory: false,
						childBaseRuleState,
						childToggledRuleState));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				var accessDeniedNode = directories[currentDirectoryIndex];
				accessDeniedNode.BaseIsAccessDenied = true;
				accessDeniedNode.ToggledIsAccessDenied = true;
				directories[currentDirectoryIndex] = accessDeniedNode;
				Interlocked.Exchange(ref hadAccessDenied, 1);
				if (isRootDirectory)
					Interlocked.Exchange(ref rootAccessDenied, 1);
			}
			catch
			{
				// Keep best-effort behavior for unreadable directories.
			}
		}

		if (directories.Count == 0)
			return new ScanResult<int>(0, rootAccessDenied == 1, hadAccessDenied == 1);

		var baseLocalVisibleFileCounts = new int[directories.Count];
		var toggledLocalVisibleFileCounts = new int[directories.Count];
		var matchingFileLocal01Counts = new int[directories.Count];
		var matchingFileLocal10Counts = new int[directories.Count];
		var matchingFileLocal11Counts = new int[directories.Count];
		var baseIsAccessDeniedByDirectory = new bool[directories.Count];
		var toggledIsAccessDeniedByDirectory = new bool[directories.Count];
		var baseRuleStates = new DirectoryToggleRuleState[directories.Count];
		var toggledRuleStates = new DirectoryToggleRuleState[directories.Count];

		for (var i = 0; i < directories.Count; i++)
		{
			baseIsAccessDeniedByDirectory[i] = directories[i].BaseIsAccessDenied;
			toggledIsAccessDeniedByDirectory[i] = directories[i].ToggledIsAccessDenied;
			baseRuleStates[i] = directories[i].BaseRuleState;
			toggledRuleStates[i] = directories[i].ToggledRuleState;
		}

		for (var index = 0; index < directories.Count; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			if (!baseRuleStates[index].CanTraverseChildren && !toggledRuleStates[index].CanTraverseChildren)
				continue;

			var dir = directories[index].Path;
			var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);
			var toggledShouldApplySmartIgnoreForFiles = toggledRules.ShouldApplySmartIgnore(dir, isDirectory: true);

			try
			{
				foreach (var file in Directory.EnumerateFiles(dir))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var name = Path.GetFileName(file);
					var fileGitIgnore = useGitIgnore
						? rules.EvaluateGitIgnore(file, isDirectory: false, name)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					var toggledFileGitIgnore = toggledUseGitIgnore
						? toggledRules.EvaluateGitIgnore(file, isDirectory: false, name)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;

					var baseVisible = ShouldTreatFileAsVisibleForTree(
						name,
						file,
						allowedExtensions,
						rules,
						shouldApplySmartIgnoreForFiles,
						fileGitIgnore);
					var toggledVisible = ShouldTreatFileAsVisibleForTree(
						name,
						file,
						allowedExtensions,
						toggledRules,
						toggledShouldApplySmartIgnoreForFiles,
						toggledFileGitIgnore);

					if (baseVisible)
						baseLocalVisibleFileCounts[index]++;
					if (toggledVisible)
						toggledLocalVisibleFileCounts[index]++;

					if (!IsFileTargetOption(optionId) || !MatchesFileTargetOption(name, file, optionId))
						continue;

					if (baseVisible)
					{
						if (toggledVisible)
							matchingFileLocal11Counts[index]++;
						else
							matchingFileLocal10Counts[index]++;
					}
					else if (toggledVisible)
					{
						matchingFileLocal01Counts[index]++;
					}
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				Interlocked.Exchange(ref hadAccessDenied, 1);
				baseIsAccessDeniedByDirectory[index] = true;
				toggledIsAccessDeniedByDirectory[index] = true;
			}
			catch
			{
				// Keep best-effort behavior for unreadable directories.
			}
		}

		var baseLocalVisibleChildCounts = new int[directories.Count];
		var toggledLocalVisibleChildCounts = new int[directories.Count];
		var baseLocalVisibilityByDirectory = new bool[directories.Count];
		var toggledLocalVisibilityByDirectory = new bool[directories.Count];

		for (var index = directories.Count - 1; index >= 0; index--)
		{
			var baseHasVisibleContent = baseIsAccessDeniedByDirectory[index] ||
			                            baseLocalVisibleFileCounts[index] > 0 ||
			                            baseLocalVisibleChildCounts[index] > 0;
			var toggledHasVisibleContent = toggledIsAccessDeniedByDirectory[index] ||
			                               toggledLocalVisibleFileCounts[index] > 0 ||
			                               toggledLocalVisibleChildCounts[index] > 0;

			var baseLocalVisible = IsDirectoryLocallyVisible(
				baseRuleStates[index],
				rules.IgnoreEmptyFolders,
				baseHasVisibleContent);
			var toggledLocalVisible = IsDirectoryLocallyVisible(
				toggledRuleStates[index],
				toggledRules.IgnoreEmptyFolders,
				toggledHasVisibleContent);

			baseLocalVisibilityByDirectory[index] = baseLocalVisible;
			toggledLocalVisibilityByDirectory[index] = toggledLocalVisible;

			var parentIndex = directories[index].ParentIndex;
			if (parentIndex < 0)
				continue;

			if (baseLocalVisible)
				baseLocalVisibleChildCounts[parentIndex]++;
			if (toggledLocalVisible)
				toggledLocalVisibleChildCounts[parentIndex]++;
		}

		var affectedNodeCount = 0;
		var baseFinalVisibilityByDirectory = new bool[directories.Count];
		var toggledFinalVisibilityByDirectory = new bool[directories.Count];

		for (var index = 0; index < directories.Count; index++)
		{
			var parentIndex = directories[index].ParentIndex;
			var baseFinalVisible = parentIndex < 0
				? baseLocalVisibilityByDirectory[index]
				: baseFinalVisibilityByDirectory[parentIndex] && baseLocalVisibilityByDirectory[index];
			var toggledFinalVisible = parentIndex < 0
				? toggledLocalVisibilityByDirectory[index]
				: toggledFinalVisibilityByDirectory[parentIndex] && toggledLocalVisibilityByDirectory[index];

			baseFinalVisibilityByDirectory[index] = baseFinalVisible;
			toggledFinalVisibilityByDirectory[index] = toggledFinalVisible;

			if (IsDirectoryTargetOption(optionId))
			{
				var directoryName = Path.GetFileName(directories[index].Path);
				if (MatchesDirectoryTargetOption(directoryName, directories[index].Path, optionId) &&
				    baseFinalVisible != toggledFinalVisible)
				{
					affectedNodeCount++;
				}
			}

			if (!IsFileTargetOption(optionId))
				continue;

			if (toggledFinalVisible)
				affectedNodeCount += matchingFileLocal01Counts[index];
			if (baseFinalVisible)
				affectedNodeCount += matchingFileLocal10Counts[index];
			if (baseFinalVisible != toggledFinalVisible)
				affectedNodeCount += matchingFileLocal11Counts[index];
		}

		return new ScanResult<int>(affectedNodeCount, rootAccessDenied == 1, hadAccessDenied == 1);
	}

	private ScanResult<int> ScanVisibleTreeNodeCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var useGitIgnore = rules.UseGitIgnore;
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var rootGitIgnore = useGitIgnore
			? rules.EvaluateGitIgnore(rootPath, isDirectory: true, rootName)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
		if (ShouldSkipDirectoryByName(rootName, rootPath, rules, rootGitIgnore))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var directories = new List<TreeVisibilityScanNode>(capacity: 256);
		var rootAccessDenied = 0;
		var hadAccessDenied = 0;

		var pending = new Stack<(string Path, int ParentIndex, bool IsRootDirectory, bool IsSelfIgnoredButTraversed)>();
		pending.Push((
			rootPath,
			ParentIndex: -1,
			IsRootDirectory: true,
			IsSelfIgnoredButTraversed: rootGitIgnore.IsIgnored && rootGitIgnore.ShouldTraverseIgnoredDirectory));

		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (dir, parentIndex, isRootDirectory, isSelfIgnoredButTraversed) = pending.Pop();
			var currentDirectoryIndex = directories.Count;
			directories.Add(new TreeVisibilityScanNode(dir, parentIndex, isAccessDenied: false, isSelfIgnoredButTraversed));

			try
			{
				foreach (var sd in Directory.EnumerateDirectories(dir))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var dirName = Path.GetFileName(sd);
					var directoryGitIgnore = useGitIgnore
						? rules.EvaluateGitIgnore(sd, isDirectory: true, dirName)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					if (ShouldSkipDirectoryByName(dirName, sd, rules, directoryGitIgnore))
						continue;

					pending.Push((
						sd,
						currentDirectoryIndex,
						IsRootDirectory: false,
						IsSelfIgnoredButTraversed: directoryGitIgnore.IsIgnored && directoryGitIgnore.ShouldTraverseIgnoredDirectory));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				var accessDeniedNode = directories[currentDirectoryIndex];
				accessDeniedNode.IsAccessDenied = true;
				directories[currentDirectoryIndex] = accessDeniedNode;
				Interlocked.Exchange(ref hadAccessDenied, 1);
				if (isRootDirectory)
					Interlocked.Exchange(ref rootAccessDenied, 1);
				continue;
			}
			catch
			{
				continue;
			}
		}

		if (directories.Count == 0)
			return new ScanResult<int>(0, rootAccessDenied == 1, hadAccessDenied == 1);

		var visibleFileCountByDirectory = new int[directories.Count];
		var visibleChildNodeCountByDirectory = new int[directories.Count];
		var isAccessDeniedByDirectory = new bool[directories.Count];
		var isSelfIgnoredButTraversedByDirectory = new bool[directories.Count];
		for (var i = 0; i < directories.Count; i++)
		{
			isAccessDeniedByDirectory[i] = directories[i].IsAccessDenied;
			isSelfIgnoredButTraversedByDirectory[i] = directories[i].IsSelfIgnoredButTraversed;
		}

		if (directories.Count < SequentialDirectoryScanThreshold)
		{
			for (var index = 0; index < directories.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var dir = directories[index].Path;
				var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);
				var visibleFileCount = 0;

				try
				{
					foreach (var file in Directory.EnumerateFiles(dir))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var name = Path.GetFileName(file);
						var fileGitIgnore = useGitIgnore
							? rules.EvaluateGitIgnore(file, isDirectory: false, name)
							: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
						if (!ShouldTreatFileAsVisibleForTree(
							    name,
							    file,
							    allowedExtensions,
							    rules,
							    shouldApplySmartIgnoreForFiles,
							    fileGitIgnore))
						{
							continue;
						}

						visibleFileCount++;
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (UnauthorizedAccessException)
				{
					Interlocked.Exchange(ref hadAccessDenied, 1);
					isAccessDeniedByDirectory[index] = true;
					continue;
				}
				catch
				{
					continue;
				}

				visibleFileCountByDirectory[index] = visibleFileCount;
			}
		}
		else
		{
			var parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Min(MaxParallelism, directories.Count),
				CancellationToken = cancellationToken
			};

			Parallel.For(
				0,
				directories.Count,
				parallelOptions,
				index =>
				{
					parallelOptions.CancellationToken.ThrowIfCancellationRequested();

					var dir = directories[index].Path;
					var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);
					var visibleFileCount = 0;

					try
					{
						foreach (var file in Directory.EnumerateFiles(dir))
						{
							parallelOptions.CancellationToken.ThrowIfCancellationRequested();

							var name = Path.GetFileName(file);
							var fileGitIgnore = useGitIgnore
								? rules.EvaluateGitIgnore(file, isDirectory: false, name)
								: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
							if (!ShouldTreatFileAsVisibleForTree(
								    name,
								    file,
								    allowedExtensions,
								    rules,
								    shouldApplySmartIgnoreForFiles,
								    fileGitIgnore))
							{
								continue;
							}

							visibleFileCount++;
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (UnauthorizedAccessException)
					{
						Interlocked.Exchange(ref hadAccessDenied, 1);
						isAccessDeniedByDirectory[index] = true;
						return;
					}
					catch
					{
						return;
					}

					visibleFileCountByDirectory[index] = visibleFileCount;
				});
		}

		var visibleTreeNodeCount = 0;
		for (var index = directories.Count - 1; index >= 0; index--)
		{
			var visibleFileCount = visibleFileCountByDirectory[index];
			var visibleChildNodeCount = visibleChildNodeCountByDirectory[index];
			var isAccessDenied = isAccessDeniedByDirectory[index];
			var isSelfIgnoredButTraversed = isSelfIgnoredButTraversedByDirectory[index];
			var parentIndex = directories[index].ParentIndex;

			// Mirror TreeBuilder semantics: explicit gitignored directories stay hidden
			// when traversal finds no visible descendants, while regular empty directories
			// remain visible unless IgnoreEmptyFolders is enabled.
			var hasVisibleContent = isAccessDenied || visibleFileCount > 0 || visibleChildNodeCount > 0;
			if (!hasVisibleContent)
			{
				if (isSelfIgnoredButTraversed)
					continue;
				if (rules.IgnoreEmptyFolders)
					continue;
			}

			var visibleSubtreeNodeCount = 1 + visibleFileCount + visibleChildNodeCount;
			if (parentIndex >= 0)
				visibleChildNodeCountByDirectory[parentIndex] += visibleSubtreeNodeCount;
			else
				visibleTreeNodeCount += visibleSubtreeNodeCount;
		}

		return new ScanResult<int>(visibleTreeNodeCount, rootAccessDenied == 1, hadAccessDenied == 1);
	}

	private ScanResult<int> ScanEffectiveEmptyFolderCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var useGitIgnore = rules.UseGitIgnore;
		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<int>(0, RootAccessDenied: false, HadAccessDenied: false);

		var directories = new List<TreeVisibilityScanNode>(capacity: 256);
		var rootAccessDenied = 0;
		var hadAccessDenied = 0;

		var pending = new Stack<(string Path, int ParentIndex, bool IsRootDirectory, bool IsSelfIgnoredButTraversed)>();
		pending.Push((rootPath, ParentIndex: -1, IsRootDirectory: true, IsSelfIgnoredButTraversed: false));

		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (dir, parentIndex, isRootDirectory, isSelfIgnoredButTraversed) = pending.Pop();
			var currentDirectoryIndex = directories.Count;
			directories.Add(new TreeVisibilityScanNode(dir, parentIndex, isAccessDenied: false, isSelfIgnoredButTraversed));

			try
			{
				foreach (var sd in Directory.EnumerateDirectories(dir))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var dirName = Path.GetFileName(sd);
					var directoryGitIgnore = useGitIgnore
						? rules.EvaluateGitIgnore(sd, isDirectory: true, dirName)
						: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
					if (ShouldSkipDirectoryByName(dirName, sd, rules, directoryGitIgnore))
						continue;

					pending.Push((
						sd,
						currentDirectoryIndex,
						IsRootDirectory: false,
						IsSelfIgnoredButTraversed: directoryGitIgnore.IsIgnored && directoryGitIgnore.ShouldTraverseIgnoredDirectory));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				var accessDeniedNode = directories[currentDirectoryIndex];
				accessDeniedNode.IsAccessDenied = true;
				directories[currentDirectoryIndex] = accessDeniedNode;
				Interlocked.Exchange(ref hadAccessDenied, 1);
				if (isRootDirectory)
					Interlocked.Exchange(ref rootAccessDenied, 1);
				continue;
			}
			catch
			{
				continue;
			}
		}

		if (directories.Count == 0)
			return new ScanResult<int>(0, rootAccessDenied == 1, hadAccessDenied == 1);

		var hasVisibleFilesByDirectory = new bool[directories.Count];
		var isAccessDeniedByDirectory = new bool[directories.Count];
		var isSelfIgnoredButTraversedByDirectory = new bool[directories.Count];
		for (var i = 0; i < directories.Count; i++)
		{
			isAccessDeniedByDirectory[i] = directories[i].IsAccessDenied;
			isSelfIgnoredButTraversedByDirectory[i] = directories[i].IsSelfIgnoredButTraversed;
		}

		if (directories.Count < SequentialDirectoryScanThreshold)
		{
			for (var index = 0; index < directories.Count; index++)
			{
				cancellationToken.ThrowIfCancellationRequested();

				var dir = directories[index].Path;
				var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);
				var hasVisibleFiles = false;

				try
				{
					foreach (var file in Directory.EnumerateFiles(dir))
					{
						cancellationToken.ThrowIfCancellationRequested();

						var name = Path.GetFileName(file);
						var fileGitIgnore = useGitIgnore
							? rules.EvaluateGitIgnore(file, isDirectory: false, name)
							: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
						if (!ShouldTreatFileAsVisibleForTree(
							    name,
							    file,
							    allowedExtensions,
							    rules,
							    shouldApplySmartIgnoreForFiles,
							    fileGitIgnore))
						{
							continue;
						}

						hasVisibleFiles = true;
						break;
					}
				}
				catch (OperationCanceledException)
				{
					throw;
				}
				catch (UnauthorizedAccessException)
				{
					Interlocked.Exchange(ref hadAccessDenied, 1);
					isAccessDeniedByDirectory[index] = true;
					continue;
				}
				catch
				{
					continue;
				}

				hasVisibleFilesByDirectory[index] = hasVisibleFiles;
			}
		}
		else
		{
			var parallelOptions = new ParallelOptions
			{
				MaxDegreeOfParallelism = Math.Min(MaxParallelism, directories.Count),
				CancellationToken = cancellationToken
			};

			Parallel.For(
				0,
				directories.Count,
				parallelOptions,
				index =>
				{
					parallelOptions.CancellationToken.ThrowIfCancellationRequested();

					var dir = directories[index].Path;
					var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(dir, isDirectory: true);
					var hasVisibleFiles = false;

					try
					{
						foreach (var file in Directory.EnumerateFiles(dir))
						{
							parallelOptions.CancellationToken.ThrowIfCancellationRequested();

							var name = Path.GetFileName(file);
							var fileGitIgnore = useGitIgnore
								? rules.EvaluateGitIgnore(file, isDirectory: false, name)
								: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
							if (!ShouldTreatFileAsVisibleForTree(
								    name,
								    file,
								    allowedExtensions,
								    rules,
								    shouldApplySmartIgnoreForFiles,
								    fileGitIgnore))
							{
								continue;
							}

							hasVisibleFiles = true;
							break;
						}
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (UnauthorizedAccessException)
					{
						Interlocked.Exchange(ref hadAccessDenied, 1);
						isAccessDeniedByDirectory[index] = true;
						return;
					}
					catch
					{
						return;
					}

					hasVisibleFilesByDirectory[index] = hasVisibleFiles;
				});
		}

		var emptyFolderCount = 0;
		var nonPrunedChildCounts = new int[directories.Count];
		for (var index = directories.Count - 1; index >= 0; index--)
		{
			var hasVisibleFiles = hasVisibleFilesByDirectory[index];
			var hasVisibleChildren = nonPrunedChildCounts[index] > 0;
			var isAccessDenied = isAccessDeniedByDirectory[index];
			var isSelfIgnoredButTraversed = isSelfIgnoredButTraversedByDirectory[index];
			var parentIndex = directories[index].ParentIndex;

			var shouldRemain = isAccessDenied || hasVisibleFiles || hasVisibleChildren;
			if (!shouldRemain)
			{
				// Directories hidden by an explicit gitignore match are controlled by
				// UseGitIgnore itself. Empty-folder counts should only reflect parents
				// whose visibility is decided by the EmptyFolders toggle.
				if (!isSelfIgnoredButTraversed)
					emptyFolderCount++;
				continue;
			}

			if (parentIndex >= 0)
				nonPrunedChildCounts[parentIndex]++;
		}

		return new ScanResult<int>(emptyFolderCount, rootAccessDenied == 1, hadAccessDenied == 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AccumulateDirectoryIgnoreOptionCounts(
		string fullPath,
		string name,
		ref MutableIgnoreOptionCounts counts)
	{
		if (IsDotName(name))
			counts.DotFolders++;
		if (HasHiddenAttribute(fullPath))
			counts.HiddenFolders++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AccumulateFileIgnoreOptionCounts(
		string fullPath,
		string name,
		ref MutableIgnoreOptionCounts counts)
	{
		if (IsExtensionlessFileName(name))
			counts.ExtensionlessFiles++;
		if (IsZeroLengthFile(fullPath))
			counts.EmptyFiles++;
		if (IsDotName(name))
			counts.DotFiles++;
		if (HasHiddenAttribute(fullPath))
			counts.HiddenFiles++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsDotName(string name)
	{
		return !string.IsNullOrEmpty(name) && name[0] == '.';
	}

	private static bool HasHiddenAttribute(string fullPath)
	{
		try
		{
			return File.GetAttributes(fullPath).HasFlag(FileAttributes.Hidden);
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool ShouldTreatFileAsVisibleForTree(
		string name,
		string fullPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		bool shouldApplySmartIgnore,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (ShouldSkipFileByName(name, fullPath, rules, shouldApplySmartIgnore, gitIgnoreEvaluation))
			return false;

		if (IsExtensionlessFileName(name))
			return true;

		if (allowedExtensions.Count == 0)
			return false;

		var ext = Path.GetExtension(name);
		return !string.IsNullOrWhiteSpace(ext) && allowedExtensions.Contains(ext);
	}

	private static bool IsZeroLengthFile(string fullPath)
	{
		try
		{
			return new FileInfo(fullPath).Length == 0;
		}
		catch
		{
			return false;
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool IsExtensionlessFileName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return false;

		// Use Span to find extension without allocation
		var span = name.AsSpan();
		var dotIndex = span.LastIndexOf('.');

		// No dot found or dot is first char (like .gitignore)
		if (dotIndex <= 0)
			return dotIndex != 0;

		// Dot is at the end (like "file.")
		return dotIndex == span.Length - 1;
	}

	private sealed class LocalExtensionScanState
	{
		public HashSet<string> Extensions { get; } = new(StringComparer.OrdinalIgnoreCase);
		public MutableIgnoreOptionCounts Counts;
	}

	private struct TreeVisibilityScanNode(
		string path,
		int parentIndex,
		bool isAccessDenied,
		bool isSelfIgnoredButTraversed)
	{
		public string Path { get; } = path;
		public int ParentIndex { get; } = parentIndex;
		public bool IsAccessDenied { get; set; } = isAccessDenied;
		public bool IsSelfIgnoredButTraversed { get; } = isSelfIgnoredButTraversed;
	}

	private struct DirectoryScanNode(string path, int parentIndex, bool isAccessDenied)
	{
		public string Path { get; } = path;
		public int ParentIndex { get; } = parentIndex;
		public bool IsAccessDenied { get; set; } = isAccessDenied;
	}

	private readonly record struct DirectoryToggleRuleState(
		bool CanTraverseChildren,
		bool IsSelfIgnoredButTraversed);

	private struct ToggleVisibilityScanNode(
		string path,
		int parentIndex,
		bool baseIsAccessDenied,
		bool toggledIsAccessDenied,
		DirectoryToggleRuleState baseRuleState,
		DirectoryToggleRuleState toggledRuleState)
	{
		public string Path { get; } = path;
		public int ParentIndex { get; } = parentIndex;
		public bool BaseIsAccessDenied { get; set; } = baseIsAccessDenied;
		public bool ToggledIsAccessDenied { get; set; } = toggledIsAccessDenied;
		public DirectoryToggleRuleState BaseRuleState { get; } = baseRuleState;
		public DirectoryToggleRuleState ToggledRuleState { get; } = toggledRuleState;
	}

	private struct MutableIgnoreOptionCounts
	{
		public int HiddenFolders;
		public int HiddenFiles;
		public int DotFolders;
		public int DotFiles;
		public int EmptyFolders;
		public int EmptyFiles;
		public int ExtensionlessFiles;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(in MutableIgnoreOptionCounts other)
		{
			HiddenFolders += other.HiddenFolders;
			HiddenFiles += other.HiddenFiles;
			DotFolders += other.DotFolders;
			DotFiles += other.DotFiles;
			EmptyFolders += other.EmptyFolders;
			EmptyFiles += other.EmptyFiles;
			ExtensionlessFiles += other.ExtensionlessFiles;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly IgnoreOptionCounts ToImmutable()
		{
			return new IgnoreOptionCounts(HiddenFolders, HiddenFiles, DotFolders, DotFiles, EmptyFolders, ExtensionlessFiles, EmptyFiles);
		}
	}
}
