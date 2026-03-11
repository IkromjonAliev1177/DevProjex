namespace DevProjex.Infrastructure.FileSystem;

public sealed class FileSystemScanner : IFileSystemScanner, IFileSystemScannerAdvanced
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

	private struct DirectoryScanNode
	{
		public DirectoryScanNode(string path, int parentIndex, bool isAccessDenied)
		{
			Path = path;
			ParentIndex = parentIndex;
			IsAccessDenied = isAccessDenied;
		}

		public string Path { get; }
		public int ParentIndex { get; }
		public bool IsAccessDenied { get; set; }
	}

	private struct MutableIgnoreOptionCounts
	{
		public int HiddenFolders;
		public int HiddenFiles;
		public int DotFolders;
		public int DotFiles;
		public int EmptyFolders;
		public int ExtensionlessFiles;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Add(in MutableIgnoreOptionCounts other)
		{
			HiddenFolders += other.HiddenFolders;
			HiddenFiles += other.HiddenFiles;
			DotFolders += other.DotFolders;
			DotFiles += other.DotFiles;
			EmptyFolders += other.EmptyFolders;
			ExtensionlessFiles += other.ExtensionlessFiles;
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public readonly IgnoreOptionCounts ToImmutable()
		{
			return new IgnoreOptionCounts(HiddenFolders, HiddenFiles, DotFolders, DotFiles, EmptyFolders, ExtensionlessFiles);
		}
	}
}
