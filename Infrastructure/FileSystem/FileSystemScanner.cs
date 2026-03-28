namespace DevProjex.Infrastructure.FileSystem;

public sealed class FileSystemScanner : IFileSystemScanner, IFileSystemScannerAdvanced, IFileSystemScannerEffectiveEmptyFolderCounter, IFileSystemScannerEffectiveIgnoreCountsProvider
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

	public ScanResult<IgnoreOptionCounts> GetEffectiveIgnoreOptionCounts(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanEffectiveIgnoreOptionCountsCore(rootPath, allowedExtensions, rules, cancellationToken);
	}

	public ScanResult<IgnoreOptionCounts> GetEffectiveRootFileIgnoreOptionCounts(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken = default)
	{
		return ScanEffectiveRootFileIgnoreOptionCountsCore(rootPath, allowedExtensions, rules, cancellationToken);
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

	private static DirectoryScanFacts AnalyzeDirectory(string fullPath, string name, IgnoreRules rules)
	{
		var gitIgnoreEvaluation = rules.UseGitIgnore
			? rules.EvaluateGitIgnore(fullPath, isDirectory: true, name)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;

		return new DirectoryScanFacts(
			Name: name,
			FullPath: fullPath,
			IsHidden: HasHiddenAttribute(fullPath),
			IsDot: IsDotName(name),
			IsSmartIgnored: rules.ShouldApplySmartIgnore(fullPath, isDirectory: true) &&
			                rules.SmartIgnoredFolders.Contains(name),
			GitIgnoreEvaluation: gitIgnoreEvaluation);
	}

	private static FileScanFacts AnalyzeFile(
		string fullPath,
		string name,
		bool shouldApplySmartIgnoreForFiles,
		IgnoreRules rules)
	{
		var isExtensionless = IsExtensionlessFileName(name);
		var gitIgnored = rules.UseGitIgnore &&
		                 rules.EvaluateGitIgnore(fullPath, isDirectory: false, name).IsIgnored;

		return new FileScanFacts(
			Name: name,
			Extension: Path.GetExtension(name),
			IsHidden: HasHiddenAttribute(fullPath),
			IsDot: IsDotName(name),
			IsEmpty: IsZeroLengthFile(fullPath),
			IsExtensionless: isExtensionless,
			IsSmartIgnored: shouldApplySmartIgnoreForFiles && rules.SmartIgnoredFiles.Contains(name),
			IsGitIgnored: gitIgnored);
	}

	private static DirectoryToggleRuleState EvaluateDirectoryRuleState(
		in DirectoryScanFacts facts,
		bool ignoreHiddenFolders,
		bool ignoreDotFolders)
	{
		if (facts.GitIgnoreEvaluation.IsIgnored && !facts.GitIgnoreEvaluation.ShouldTraverseIgnoredDirectory)
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (facts.IsSmartIgnored)
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (ignoreDotFolders && facts.IsDot)
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		if (ignoreHiddenFolders && facts.IsHidden)
			return new DirectoryToggleRuleState(CanTraverseChildren: false, IsSelfIgnoredButTraversed: false);

		return new DirectoryToggleRuleState(
			CanTraverseChildren: true,
			IsSelfIgnoredButTraversed: facts.GitIgnoreEvaluation.IsIgnored &&
			                           facts.GitIgnoreEvaluation.ShouldTraverseIgnoredDirectory);
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

	private static EffectiveFileVisibilityProfile EvaluateFileVisibilityProfile(
		in FileScanFacts facts,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules)
	{
		if (facts.IsGitIgnored || facts.IsSmartIgnored || !PassesExtensionFilter(facts, allowedExtensions))
			return default;

		return new EffectiveFileVisibilityProfile(
			BaseVisible: PassesFileIgnoreRules(
				facts,
				rules.IgnoreHiddenFiles,
				rules.IgnoreDotFiles,
				rules.IgnoreEmptyFiles,
				rules.IgnoreExtensionlessFiles),
			HiddenFilesVisible: PassesFileIgnoreRules(
				facts,
				!rules.IgnoreHiddenFiles,
				rules.IgnoreDotFiles,
				rules.IgnoreEmptyFiles,
				rules.IgnoreExtensionlessFiles),
			DotFilesVisible: PassesFileIgnoreRules(
				facts,
				rules.IgnoreHiddenFiles,
				!rules.IgnoreDotFiles,
				rules.IgnoreEmptyFiles,
				rules.IgnoreExtensionlessFiles),
			EmptyFilesVisible: PassesFileIgnoreRules(
				facts,
				rules.IgnoreHiddenFiles,
				rules.IgnoreDotFiles,
				!rules.IgnoreEmptyFiles,
				rules.IgnoreExtensionlessFiles),
			ExtensionlessFilesVisible: PassesFileIgnoreRules(
				facts,
				rules.IgnoreHiddenFiles,
				rules.IgnoreDotFiles,
				rules.IgnoreEmptyFiles,
				!rules.IgnoreExtensionlessFiles));
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesExtensionFilter(in FileScanFacts facts, IReadOnlySet<string> allowedExtensions)
	{
		if (facts.IsExtensionless)
			return true;

		return allowedExtensions.Count > 0 &&
		       !string.IsNullOrWhiteSpace(facts.Extension) &&
		       allowedExtensions.Contains(facts.Extension);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool PassesFileIgnoreRules(
		in FileScanFacts facts,
		bool ignoreHiddenFiles,
		bool ignoreDotFiles,
		bool ignoreEmptyFiles,
		bool ignoreExtensionlessFiles)
	{
		if (ignoreHiddenFiles && facts.IsHidden)
			return false;
		if (ignoreDotFiles && facts.IsDot)
			return false;
		if (ignoreEmptyFiles && facts.IsEmpty)
			return false;
		if (ignoreExtensionlessFiles && facts.IsExtensionless)
			return false;

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

	private ScanResult<IgnoreOptionCounts> ScanEffectiveRootFileIgnoreOptionCountsCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<IgnoreOptionCounts>(IgnoreOptionCounts.Empty, RootAccessDenied: false, HadAccessDenied: false);

		var counts = default(MutableIgnoreOptionCounts);
		var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(rootPath, isDirectory: true);

		try
		{
			foreach (var file in Directory.EnumerateFiles(rootPath))
			{
				cancellationToken.ThrowIfCancellationRequested();

				var name = Path.GetFileName(file);
				var facts = AnalyzeFile(file, name, shouldApplySmartIgnoreForFiles, rules);
				var visibility = EvaluateFileVisibilityProfile(facts, allowedExtensions, rules);

				AccumulateDirectFileDelta(facts.IsHidden, visibility.BaseVisible, visibility.HiddenFilesVisible, ref counts.HiddenFiles);
				AccumulateDirectFileDelta(facts.IsDot, visibility.BaseVisible, visibility.DotFilesVisible, ref counts.DotFiles);
				AccumulateDirectFileDelta(facts.IsEmpty, visibility.BaseVisible, visibility.EmptyFilesVisible, ref counts.EmptyFiles);
				AccumulateDirectFileDelta(
					facts.IsExtensionless,
					visibility.BaseVisible,
					visibility.ExtensionlessFilesVisible,
					ref counts.ExtensionlessFiles);
			}
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (UnauthorizedAccessException)
		{
			return new ScanResult<IgnoreOptionCounts>(IgnoreOptionCounts.Empty, RootAccessDenied: true, HadAccessDenied: true);
		}
		catch
		{
			return new ScanResult<IgnoreOptionCounts>(IgnoreOptionCounts.Empty, RootAccessDenied: false, HadAccessDenied: false);
		}

		return new ScanResult<IgnoreOptionCounts>(counts.ToImmutable(), RootAccessDenied: false, HadAccessDenied: false);
	}

	private ScanResult<IgnoreOptionCounts> ScanEffectiveIgnoreOptionCountsCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var discovery = DiscoverEffectiveIgnoreScanNodes(rootPath, rules, cancellationToken);
		var directories = discovery.Value;
		if (directories.Count == 0)
			return new ScanResult<IgnoreOptionCounts>(IgnoreOptionCounts.Empty, discovery.RootAccessDenied, discovery.HadAccessDenied);

		var fileMetrics = new EffectiveIgnoreNodeFileMetrics[directories.Count];
		var visibilityStates = new EffectiveIgnoreNodeVisibilityState[directories.Count];
		var hadAccessDenied = discovery.HadAccessDenied ? 1 : 0;

		for (var index = 0; index < directories.Count; index++)
			visibilityStates[index].IsAccessDenied = directories[index].IsAccessDenied;

		void ScanDirectoryFiles(int index, CancellationToken token)
		{
			token.ThrowIfCancellationRequested();

			var node = directories[index];
			if (!node.CanAnyVariantTraverseChildren)
				return;

			var shouldApplySmartIgnoreForFiles = rules.ShouldApplySmartIgnore(node.Path, isDirectory: true);
			var localMetrics = default(EffectiveIgnoreNodeFileMetrics);

			try
			{
				foreach (var file in Directory.EnumerateFiles(node.Path))
				{
					token.ThrowIfCancellationRequested();

					var name = Path.GetFileName(file);
					var facts = AnalyzeFile(file, name, shouldApplySmartIgnoreForFiles, rules);
					var visibility = EvaluateFileVisibilityProfile(facts, allowedExtensions, rules);

					if (visibility.BaseVisible)
						localMetrics.BaseVisibleFiles++;
					if (visibility.HiddenFilesVisible)
						localMetrics.HiddenFilesVisibleFiles++;
					if (visibility.DotFilesVisible)
						localMetrics.DotFilesVisibleFiles++;
					if (visibility.EmptyFilesVisible)
						localMetrics.EmptyFilesVisibleFiles++;
					if (visibility.ExtensionlessFilesVisible)
						localMetrics.ExtensionlessFilesVisibleFiles++;

					AccumulateToggleTransition(
						facts.IsHidden,
						visibility.BaseVisible,
						visibility.HiddenFilesVisible,
						ref localMetrics.HiddenFilesAppearWhenToggled,
						ref localMetrics.HiddenFilesDisappearWhenToggled);
					AccumulateToggleTransition(
						facts.IsDot,
						visibility.BaseVisible,
						visibility.DotFilesVisible,
						ref localMetrics.DotFilesAppearWhenToggled,
						ref localMetrics.DotFilesDisappearWhenToggled);
					AccumulateToggleTransition(
						facts.IsEmpty,
						visibility.BaseVisible,
						visibility.EmptyFilesVisible,
						ref localMetrics.EmptyFilesAppearWhenToggled,
						ref localMetrics.EmptyFilesDisappearWhenToggled);
					AccumulateToggleTransition(
						facts.IsExtensionless,
						visibility.BaseVisible,
						visibility.ExtensionlessFilesVisible,
						ref localMetrics.ExtensionlessFilesAppearWhenToggled,
						ref localMetrics.ExtensionlessFilesDisappearWhenToggled);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				Interlocked.Exchange(ref hadAccessDenied, 1);
				var visibilityState = visibilityStates[index];
				visibilityState.IsAccessDenied = true;
				visibilityStates[index] = visibilityState;
				return;
			}
			catch
			{
				return;
			}

			fileMetrics[index] = localMetrics;
		}

		if (directories.Count < SequentialDirectoryScanThreshold)
		{
			for (var index = 0; index < directories.Count; index++)
				ScanDirectoryFiles(index, cancellationToken);
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
				index => ScanDirectoryFiles(index, parallelOptions.CancellationToken));
		}

		for (var index = directories.Count - 1; index >= 0; index--)
		{
			var node = directories[index];
			var visibilityState = visibilityStates[index];
			var metrics = fileMetrics[index];

			var baseHasVisibleContent = visibilityState.IsAccessDenied ||
			                            metrics.BaseVisibleFiles > 0 ||
			                            visibilityState.BaseVisibleChildren > 0;
			var hiddenFoldersHasVisibleContent = visibilityState.IsAccessDenied ||
			                                     metrics.BaseVisibleFiles > 0 ||
			                                     visibilityState.HiddenFoldersVisibleChildren > 0;
			var dotFoldersHasVisibleContent = visibilityState.IsAccessDenied ||
			                                  metrics.BaseVisibleFiles > 0 ||
			                                  visibilityState.DotFoldersVisibleChildren > 0;
			var hiddenFilesHasVisibleContent = visibilityState.IsAccessDenied ||
			                                   metrics.HiddenFilesVisibleFiles > 0 ||
			                                   visibilityState.HiddenFilesVisibleChildren > 0;
			var dotFilesHasVisibleContent = visibilityState.IsAccessDenied ||
			                                metrics.DotFilesVisibleFiles > 0 ||
			                                visibilityState.DotFilesVisibleChildren > 0;
			var emptyFilesHasVisibleContent = visibilityState.IsAccessDenied ||
			                                  metrics.EmptyFilesVisibleFiles > 0 ||
			                                  visibilityState.EmptyFilesVisibleChildren > 0;
			var extensionlessFilesHasVisibleContent = visibilityState.IsAccessDenied ||
			                                          metrics.ExtensionlessFilesVisibleFiles > 0 ||
			                                          visibilityState.ExtensionlessFilesVisibleChildren > 0;
			var emptyFoldersHasVisibleContent = visibilityState.IsAccessDenied ||
			                                    metrics.BaseVisibleFiles > 0 ||
			                                    visibilityState.EmptyFoldersVisibleChildren > 0;

			visibilityState.BaseLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				rules.IgnoreEmptyFolders,
				baseHasVisibleContent);
			visibilityState.HiddenFoldersLocalVisible = IsDirectoryLocallyVisible(
				node.HiddenFoldersRuleState,
				rules.IgnoreEmptyFolders,
				hiddenFoldersHasVisibleContent);
			visibilityState.DotFoldersLocalVisible = IsDirectoryLocallyVisible(
				node.DotFoldersRuleState,
				rules.IgnoreEmptyFolders,
				dotFoldersHasVisibleContent);
			visibilityState.HiddenFilesLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				rules.IgnoreEmptyFolders,
				hiddenFilesHasVisibleContent);
			visibilityState.DotFilesLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				rules.IgnoreEmptyFolders,
				dotFilesHasVisibleContent);
			visibilityState.EmptyFilesLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				rules.IgnoreEmptyFolders,
				emptyFilesHasVisibleContent);
			visibilityState.ExtensionlessFilesLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				rules.IgnoreEmptyFolders,
				extensionlessFilesHasVisibleContent);
			visibilityState.EmptyFoldersLocalVisible = IsDirectoryLocallyVisible(
				node.BaseRuleState,
				!rules.IgnoreEmptyFolders,
				emptyFoldersHasVisibleContent);
			visibilityStates[index] = visibilityState;

			if (node.ParentIndex < 0)
				continue;

			var parentVisibilityState = visibilityStates[node.ParentIndex];
			if (visibilityState.BaseLocalVisible)
				parentVisibilityState.BaseVisibleChildren++;
			if (visibilityState.HiddenFoldersLocalVisible)
				parentVisibilityState.HiddenFoldersVisibleChildren++;
			if (visibilityState.DotFoldersLocalVisible)
				parentVisibilityState.DotFoldersVisibleChildren++;
			if (visibilityState.HiddenFilesLocalVisible)
				parentVisibilityState.HiddenFilesVisibleChildren++;
			if (visibilityState.DotFilesLocalVisible)
				parentVisibilityState.DotFilesVisibleChildren++;
			if (visibilityState.EmptyFilesLocalVisible)
				parentVisibilityState.EmptyFilesVisibleChildren++;
			if (visibilityState.ExtensionlessFilesLocalVisible)
				parentVisibilityState.ExtensionlessFilesVisibleChildren++;
			if (visibilityState.EmptyFoldersLocalVisible)
				parentVisibilityState.EmptyFoldersVisibleChildren++;
			visibilityStates[node.ParentIndex] = parentVisibilityState;
		}

		var effectiveCounts = default(MutableIgnoreOptionCounts);
		for (var index = 0; index < directories.Count; index++)
		{
			var node = directories[index];
			var visibilityState = visibilityStates[index];
			var metrics = fileMetrics[index];

			if (node.ParentIndex < 0)
			{
				visibilityState.BaseFinalVisible = visibilityState.BaseLocalVisible;
				visibilityState.HiddenFoldersFinalVisible = visibilityState.HiddenFoldersLocalVisible;
				visibilityState.DotFoldersFinalVisible = visibilityState.DotFoldersLocalVisible;
				visibilityState.HiddenFilesFinalVisible = visibilityState.HiddenFilesLocalVisible;
				visibilityState.DotFilesFinalVisible = visibilityState.DotFilesLocalVisible;
				visibilityState.EmptyFilesFinalVisible = visibilityState.EmptyFilesLocalVisible;
				visibilityState.ExtensionlessFilesFinalVisible = visibilityState.ExtensionlessFilesLocalVisible;
				visibilityState.EmptyFoldersFinalVisible = visibilityState.EmptyFoldersLocalVisible;
			}
			else
			{
				var parentVisibilityState = visibilityStates[node.ParentIndex];
				visibilityState.BaseFinalVisible = parentVisibilityState.BaseFinalVisible &&
				                                  visibilityState.BaseLocalVisible;
				visibilityState.HiddenFoldersFinalVisible = parentVisibilityState.HiddenFoldersFinalVisible &&
				                                           visibilityState.HiddenFoldersLocalVisible;
				visibilityState.DotFoldersFinalVisible = parentVisibilityState.DotFoldersFinalVisible &&
				                                        visibilityState.DotFoldersLocalVisible;
				visibilityState.HiddenFilesFinalVisible = parentVisibilityState.HiddenFilesFinalVisible &&
				                                         visibilityState.HiddenFilesLocalVisible;
				visibilityState.DotFilesFinalVisible = parentVisibilityState.DotFilesFinalVisible &&
				                                      visibilityState.DotFilesLocalVisible;
				visibilityState.EmptyFilesFinalVisible = parentVisibilityState.EmptyFilesFinalVisible &&
				                                        visibilityState.EmptyFilesLocalVisible;
				visibilityState.ExtensionlessFilesFinalVisible = parentVisibilityState.ExtensionlessFilesFinalVisible &&
				                                                 visibilityState.ExtensionlessFilesLocalVisible;
				visibilityState.EmptyFoldersFinalVisible = parentVisibilityState.EmptyFoldersFinalVisible &&
				                                          visibilityState.EmptyFoldersLocalVisible;
			}

			visibilityStates[index] = visibilityState;

			if (node.IsHidden &&
			    visibilityState.BaseFinalVisible != visibilityState.HiddenFoldersFinalVisible)
			{
				effectiveCounts.HiddenFolders++;
			}

			if (node.IsDot &&
			    visibilityState.BaseFinalVisible != visibilityState.DotFoldersFinalVisible)
			{
				effectiveCounts.DotFolders++;
			}

			if (visibilityState.BaseFinalVisible)
			{
				effectiveCounts.HiddenFiles += metrics.HiddenFilesDisappearWhenToggled;
				effectiveCounts.DotFiles += metrics.DotFilesDisappearWhenToggled;
				effectiveCounts.EmptyFiles += metrics.EmptyFilesDisappearWhenToggled;
				effectiveCounts.ExtensionlessFiles += metrics.ExtensionlessFilesDisappearWhenToggled;
			}

			if (visibilityState.HiddenFilesFinalVisible)
				effectiveCounts.HiddenFiles += metrics.HiddenFilesAppearWhenToggled;
			if (visibilityState.DotFilesFinalVisible)
				effectiveCounts.DotFiles += metrics.DotFilesAppearWhenToggled;
			if (visibilityState.EmptyFilesFinalVisible)
				effectiveCounts.EmptyFiles += metrics.EmptyFilesAppearWhenToggled;
			if (visibilityState.ExtensionlessFilesFinalVisible)
				effectiveCounts.ExtensionlessFiles += metrics.ExtensionlessFilesAppearWhenToggled;

			if (visibilityState.BaseFinalVisible != visibilityState.EmptyFoldersFinalVisible)
				effectiveCounts.EmptyFolders++;
		}

		return new ScanResult<IgnoreOptionCounts>(
			effectiveCounts.ToImmutable(),
			discovery.RootAccessDenied,
			hadAccessDenied == 1);
	}

	private ScanResult<List<EffectiveIgnoreScanNode>> DiscoverEffectiveIgnoreScanNodes(
		string rootPath,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
			return new ScanResult<List<EffectiveIgnoreScanNode>>([], RootAccessDenied: false, HadAccessDenied: false);

		var rootName = Path.GetFileName(rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		var rootFacts = AnalyzeDirectory(rootPath, rootName, rules);
		var rootBaseRuleState = EvaluateDirectoryRuleState(rootFacts, rules.IgnoreHiddenFolders, rules.IgnoreDotFolders);
		var rootHiddenFoldersRuleState = EvaluateDirectoryRuleState(rootFacts, !rules.IgnoreHiddenFolders, rules.IgnoreDotFolders);
		var rootDotFoldersRuleState = EvaluateDirectoryRuleState(rootFacts, rules.IgnoreHiddenFolders, !rules.IgnoreDotFolders);

		if (!CanAnyVariantTraverseChildren(rootBaseRuleState, rootHiddenFoldersRuleState, rootDotFoldersRuleState))
			return new ScanResult<List<EffectiveIgnoreScanNode>>([], RootAccessDenied: false, HadAccessDenied: false);

		var directories = new List<EffectiveIgnoreScanNode>(capacity: 256);
		var rootAccessDenied = 0;
		var hadAccessDenied = 0;
		var pending =
			new Stack<(DirectoryScanFacts Facts, int ParentIndex, bool IsRootDirectory, DirectoryToggleRuleState BaseRuleState, DirectoryToggleRuleState HiddenFoldersRuleState, DirectoryToggleRuleState DotFoldersRuleState)>();
		pending.Push((
			rootFacts,
			ParentIndex: -1,
			IsRootDirectory: true,
			rootBaseRuleState,
			rootHiddenFoldersRuleState,
			rootDotFoldersRuleState));

		while (pending.Count > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var (facts, parentIndex, isRootDirectory, baseRuleState, hiddenFoldersRuleState, dotFoldersRuleState) = pending.Pop();
			var currentDirectoryIndex = directories.Count;
			directories.Add(new EffectiveIgnoreScanNode(
				facts.FullPath,
				facts.Name,
				parentIndex,
				isAccessDenied: false,
				facts.IsHidden,
				facts.IsDot,
				baseRuleState,
				hiddenFoldersRuleState,
				dotFoldersRuleState));

			if (!CanAnyVariantTraverseChildren(baseRuleState, hiddenFoldersRuleState, dotFoldersRuleState))
				continue;

			try
			{
				foreach (var childDirectory in Directory.EnumerateDirectories(facts.FullPath))
				{
					cancellationToken.ThrowIfCancellationRequested();

					var childName = Path.GetFileName(childDirectory);
					var childFacts = AnalyzeDirectory(childDirectory, childName, rules);
					var childBaseRuleState = EvaluateDirectoryRuleState(
						childFacts,
						rules.IgnoreHiddenFolders,
						rules.IgnoreDotFolders);
					var childHiddenFoldersRuleState = EvaluateDirectoryRuleState(
						childFacts,
						!rules.IgnoreHiddenFolders,
						rules.IgnoreDotFolders);
					var childDotFoldersRuleState = EvaluateDirectoryRuleState(
						childFacts,
						rules.IgnoreHiddenFolders,
						!rules.IgnoreDotFolders);

					if (!CanAnyVariantTraverseChildren(
						    childBaseRuleState,
						    childHiddenFoldersRuleState,
						    childDotFoldersRuleState))
					{
						continue;
					}

					pending.Push((
						childFacts,
						currentDirectoryIndex,
						IsRootDirectory: false,
						childBaseRuleState,
						childHiddenFoldersRuleState,
						childDotFoldersRuleState));
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (UnauthorizedAccessException)
			{
				var deniedNode = directories[currentDirectoryIndex];
				deniedNode.IsAccessDenied = true;
				directories[currentDirectoryIndex] = deniedNode;
				Interlocked.Exchange(ref hadAccessDenied, 1);
				if (isRootDirectory)
					Interlocked.Exchange(ref rootAccessDenied, 1);
			}
			catch
			{
				// Keep best-effort behavior for partial filesystem reads.
			}
		}

		return new ScanResult<List<EffectiveIgnoreScanNode>>(directories, rootAccessDenied == 1, hadAccessDenied == 1);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static bool CanAnyVariantTraverseChildren(
		DirectoryToggleRuleState baseRuleState,
		DirectoryToggleRuleState hiddenFoldersRuleState,
		DirectoryToggleRuleState dotFoldersRuleState)
	{
		return baseRuleState.CanTraverseChildren ||
		       hiddenFoldersRuleState.CanTraverseChildren ||
		       dotFoldersRuleState.CanTraverseChildren;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AccumulateDirectFileDelta(
		bool matchesTargetRule,
		bool baseVisible,
		bool toggledVisible,
		ref int count)
	{
		if (matchesTargetRule && baseVisible != toggledVisible)
			count++;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static void AccumulateToggleTransition(
		bool matchesTargetRule,
		bool baseVisible,
		bool toggledVisible,
		ref int appearsWhenToggled,
		ref int disappearsWhenToggled)
	{
		if (!matchesTargetRule || baseVisible == toggledVisible)
			return;

		if (toggledVisible)
			appearsWhenToggled++;
		else
			disappearsWhenToggled++;
	}

	private ScanResult<int> ScanEffectiveEmptyFolderCountCore(
		string rootPath,
		IReadOnlySet<string> allowedExtensions,
		IgnoreRules rules,
		CancellationToken cancellationToken)
	{
		var scan = ScanEffectiveIgnoreOptionCountsCore(rootPath, allowedExtensions, rules, cancellationToken);
		return new ScanResult<int>(scan.Value.EmptyFolders, scan.RootAccessDenied, scan.HadAccessDenied);
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

	private struct DirectoryScanNode(string path, int parentIndex, bool isAccessDenied)
	{
		public string Path { get; } = path;
		public int ParentIndex { get; } = parentIndex;
		public bool IsAccessDenied { get; set; } = isAccessDenied;
	}

	private readonly record struct DirectoryToggleRuleState(
		bool CanTraverseChildren,
		bool IsSelfIgnoredButTraversed);

	private readonly record struct DirectoryScanFacts(
		string Name,
		string FullPath,
		bool IsHidden,
		bool IsDot,
		bool IsSmartIgnored,
		IgnoreRules.GitIgnoreEvaluation GitIgnoreEvaluation);

	private readonly record struct FileScanFacts(
		string Name,
		string Extension,
		bool IsHidden,
		bool IsDot,
		bool IsEmpty,
		bool IsExtensionless,
		bool IsSmartIgnored,
		bool IsGitIgnored);

	private readonly record struct EffectiveFileVisibilityProfile(
		bool BaseVisible,
		bool HiddenFilesVisible,
		bool DotFilesVisible,
		bool EmptyFilesVisible,
		bool ExtensionlessFilesVisible);

	private struct EffectiveIgnoreScanNode(
		string path,
		string name,
		int parentIndex,
		bool isAccessDenied,
		bool isHidden,
		bool isDot,
		DirectoryToggleRuleState baseRuleState,
		DirectoryToggleRuleState hiddenFoldersRuleState,
		DirectoryToggleRuleState dotFoldersRuleState)
	{
		public string Path { get; } = path;
		public string Name { get; } = name;
		public int ParentIndex { get; } = parentIndex;
		public bool IsAccessDenied { get; set; } = isAccessDenied;
		public bool IsHidden { get; } = isHidden;
		public bool IsDot { get; } = isDot;
		public DirectoryToggleRuleState BaseRuleState { get; } = baseRuleState;
		public DirectoryToggleRuleState HiddenFoldersRuleState { get; } = hiddenFoldersRuleState;
		public DirectoryToggleRuleState DotFoldersRuleState { get; } = dotFoldersRuleState;

		public bool CanAnyVariantTraverseChildren =>
			BaseRuleState.CanTraverseChildren ||
			HiddenFoldersRuleState.CanTraverseChildren ||
			DotFoldersRuleState.CanTraverseChildren;
	}

	private struct EffectiveIgnoreNodeFileMetrics
	{
		public int BaseVisibleFiles;
		public int HiddenFilesVisibleFiles;
		public int DotFilesVisibleFiles;
		public int EmptyFilesVisibleFiles;
		public int ExtensionlessFilesVisibleFiles;
		public int HiddenFilesAppearWhenToggled;
		public int HiddenFilesDisappearWhenToggled;
		public int DotFilesAppearWhenToggled;
		public int DotFilesDisappearWhenToggled;
		public int EmptyFilesAppearWhenToggled;
		public int EmptyFilesDisappearWhenToggled;
		public int ExtensionlessFilesAppearWhenToggled;
		public int ExtensionlessFilesDisappearWhenToggled;
	}

	private struct EffectiveIgnoreNodeVisibilityState
	{
		public bool IsAccessDenied;
		public int BaseVisibleChildren;
		public int HiddenFoldersVisibleChildren;
		public int DotFoldersVisibleChildren;
		public int HiddenFilesVisibleChildren;
		public int DotFilesVisibleChildren;
		public int EmptyFilesVisibleChildren;
		public int ExtensionlessFilesVisibleChildren;
		public int EmptyFoldersVisibleChildren;
		public bool BaseLocalVisible;
		public bool HiddenFoldersLocalVisible;
		public bool DotFoldersLocalVisible;
		public bool HiddenFilesLocalVisible;
		public bool DotFilesLocalVisible;
		public bool EmptyFilesLocalVisible;
		public bool ExtensionlessFilesLocalVisible;
		public bool EmptyFoldersLocalVisible;
		public bool BaseFinalVisible;
		public bool HiddenFoldersFinalVisible;
		public bool DotFoldersFinalVisible;
		public bool HiddenFilesFinalVisible;
		public bool DotFilesFinalVisible;
		public bool EmptyFilesFinalVisible;
		public bool ExtensionlessFilesFinalVisible;
		public bool EmptyFoldersFinalVisible;
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
