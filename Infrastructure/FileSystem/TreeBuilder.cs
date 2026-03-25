namespace DevProjex.Infrastructure.FileSystem;

public sealed class TreeBuilder : ITreeBuilder
{
	// Pre-allocated comparer instance to avoid allocation per sort
	private static readonly FileSystemInfoComparer EntryComparer = new();
	private static readonly int RootBuildParallelism =
		Math.Clamp(Environment.ProcessorCount, min: 2, max: 16);

	public TreeBuildResult Build(string rootPath, TreeFilterOptions options, CancellationToken cancellationToken = default)
	{
		var state = new BuildState();

		var rootInfo = new DirectoryInfo(rootPath);
		var root = new FileSystemNode(
			name: rootInfo.Name,
			fullPath: rootPath,
			isDirectory: true,
			isAccessDenied: false,
			children: new List<FileSystemNode>());

		BuildChildren(
			parent: root,
			path: rootPath,
			options: options,
			isRoot: true,
			state: state,
			cancellationToken: cancellationToken);

		return new TreeBuildResult(root, state.RootAccessDenied, state.HadAccessDenied);
	}

	private static void BuildChildren(
		FileSystemNode parent,
		string path,
		TreeFilterOptions options,
		bool isRoot,
		BuildState state,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		FileSystemInfo[] entries;
		try
		{
			// Get entries and sort in-place - avoids LINQ allocations
			entries = new DirectoryInfo(path).GetFileSystemInfos();
			Array.Sort(entries, EntryComparer);
		}
		catch (UnauthorizedAccessException)
		{
			if (isRoot)
				state.MarkRootAccessDenied();
			else
				state.MarkAccessDenied();
			parent.IsAccessDenied = true;
			return;
		}
		catch
		{
			return;
		}

		var children = (List<FileSystemNode>)parent.Children;
		var hasNameFilter = !string.IsNullOrWhiteSpace(options.NameFilter);
		var shouldApplySmartIgnoreForFiles = options.IgnoreRules.ShouldApplySmartIgnore(path, isDirectory: true);

		if (isRoot && entries.Length > 1)
		{
			BuildRootChildrenInParallel(
				entries,
				children,
				options,
				hasNameFilter,
				shouldApplySmartIgnoreForFiles,
				state,
				cancellationToken);
			return;
		}

		foreach (var entry in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var node = BuildNodeForEntry(
				entry,
				options,
				isRoot,
				hasNameFilter,
				shouldApplySmartIgnoreForFiles,
				state,
				cancellationToken);
			if (node is not null)
				children.Add(node);
		}
	}

	private static void BuildRootChildrenInParallel(
		FileSystemInfo[] entries,
		List<FileSystemNode> children,
		TreeFilterOptions options,
		bool hasNameFilter,
		bool shouldApplySmartIgnoreForFiles,
		BuildState state,
		CancellationToken cancellationToken)
	{
		var nodes = new FileSystemNode?[entries.Length];
		var maxDegree = Math.Min(RootBuildParallelism, entries.Length);
		var parallelOptions = new ParallelOptions
		{
			MaxDegreeOfParallelism = maxDegree,
			CancellationToken = cancellationToken
		};

		Parallel.For(0, entries.Length, parallelOptions, i =>
		{
			var entry = entries[i];
			nodes[i] = BuildNodeForEntry(
				entry,
				options,
				isRoot: true,
				hasNameFilter,
				shouldApplySmartIgnoreForFiles,
				state,
				parallelOptions.CancellationToken);
		});

		for (var i = 0; i < nodes.Length; i++)
		{
			var node = nodes[i];
			if (node is not null)
				children.Add(node);
		}
	}

	private static FileSystemNode? BuildNodeForEntry(
		FileSystemInfo entry,
		TreeFilterOptions options,
		bool isRoot,
		bool hasNameFilter,
		bool shouldApplySmartIgnoreForFiles,
		BuildState state,
		CancellationToken cancellationToken)
	{
		var name = entry.Name;
		bool isDir = IsDirectory(entry);

		if (isDir && isRoot && !options.AllowedRootFolders.Contains(name))
			return null;

		var ignore = options.IgnoreRules;
		if (isDir)
		{
			var directoryGitIgnore = ignore.UseGitIgnore
				? ignore.EvaluateGitIgnore(entry.FullName, isDirectory: true, name)
				: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
			if (ShouldSkipDirectory(entry, ignore, directoryGitIgnore))
				return null;

			var dirNode = new FileSystemNode(
				name: name,
				fullPath: entry.FullName,
				isDirectory: true,
				isAccessDenied: false,
				children: new List<FileSystemNode>());

			BuildChildren(dirNode, entry.FullName, options, isRoot: false, state, cancellationToken);

			if (ignore.IgnoreEmptyFolders &&
			    dirNode.Children.Count == 0 &&
			    !dirNode.IsAccessDenied)
			{
				return null;
			}

			// Keep full directory context when extension/ignore filters remove all files.
			// Name filter remains strict to preserve intentional narrowing behavior.
			if (hasNameFilter)
			{
				bool hasMatchingChildren = dirNode.Children.Count > 0;
				bool matchesName = name.Contains(options.NameFilter!, StringComparison.OrdinalIgnoreCase);
				return (hasMatchingChildren || matchesName) ? dirNode : null;
			}

			// Keep ignored directories out of UI when traversal found no visible descendants.
			// Parents that only became empty after descendant filtering remain visible until
			// IgnoreEmptyFolders explicitly removes them.
			if (directoryGitIgnore.IsIgnored &&
			    directoryGitIgnore.ShouldTraverseIgnoredDirectory &&
			    dirNode.Children.Count == 0 &&
			    !dirNode.IsAccessDenied)
			{
				return null;
			}

			return dirNode;
		}

		var fileGitIgnore = ignore.UseGitIgnore
			? ignore.EvaluateGitIgnore(entry.FullName, isDirectory: false, name)
			: IgnoreRules.GitIgnoreEvaluation.NotIgnored;
		if (ShouldSkipFile(entry, ignore, shouldApplySmartIgnoreForFiles, fileGitIgnore))
			return null;

		if (IsExtensionlessFileName(name))
		{
			// Extensionless files are intentionally controlled only by ignore options.
		}
		else
		{
			if (options.AllowedExtensions.Count == 0)
				return null;

			var ext = Path.GetExtension(name);
			if (!options.AllowedExtensions.Contains(ext))
				return null;
		}

		if (hasNameFilter && !name.Contains(options.NameFilter!, StringComparison.OrdinalIgnoreCase))
			return null;

		return new FileSystemNode(
			name: name,
			fullPath: entry.FullName,
			isDirectory: false,
			isAccessDenied: false,
			children: FileSystemNode.EmptyChildren);
	}

	private static bool IsDirectory(FileSystemInfo entry)
	{
		try
		{
			return entry.Attributes.HasFlag(FileAttributes.Directory);
		}
		catch (IOException)
		{
			// Reserved Windows device names (nul, con, prn, etc.) throw IOException
			// Treat them as files (non-directories)
			return false;
		}
		catch (UnauthorizedAccessException)
		{
			return false;
		}
	}

	private static bool ShouldSkipDirectory(
		FileSystemInfo entry,
		IgnoreRules rules,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (gitIgnoreEvaluation.IsIgnored)
		{
			if (!gitIgnoreEvaluation.ShouldTraverseIgnoredDirectory)
				return true;
		}

		if (rules.ShouldApplySmartIgnore(entry.FullName, isDirectory: true) && rules.SmartIgnoredFolders.Contains(entry.Name))
			return true;

		if (rules.IgnoreDotFolders && entry.Name.StartsWith(".", StringComparison.Ordinal))
			return true;

		if (rules.IgnoreHiddenFolders)
		{
			try
			{
				if (entry.Attributes.HasFlag(FileAttributes.Hidden))
					return true;
			}
			catch (IOException)
			{
				// Reserved Windows device names (nul, con, prn, etc.) throw IOException
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return true;
			}
		}

		return false;
	}

	private static bool ShouldSkipFile(
		FileSystemInfo entry,
		IgnoreRules rules,
		bool shouldApplySmartIgnoreForFiles,
		in IgnoreRules.GitIgnoreEvaluation gitIgnoreEvaluation)
	{
		if (gitIgnoreEvaluation.IsIgnored)
			return true;

		if (shouldApplySmartIgnoreForFiles && rules.SmartIgnoredFiles.Contains(entry.Name))
			return true;

		if (rules.IgnoreDotFiles && entry.Name.StartsWith(".", StringComparison.Ordinal))
			return true;

		if (rules.IgnoreExtensionlessFiles && IsExtensionlessFileName(entry.Name))
			return true;

		if (rules.IgnoreEmptyFiles && IsZeroLengthFile(entry))
			return true;

		if (rules.IgnoreHiddenFiles)
		{
			try
			{
				if (entry.Attributes.HasFlag(FileAttributes.Hidden))
					return true;
			}
			catch (IOException)
			{
				// Reserved Windows device names (nul, con, prn, etc.) throw IOException
				return true;
			}
			catch (UnauthorizedAccessException)
			{
				return true;
			}
		}

		return false;
	}

	private static bool IsExtensionlessFileName(string fileName)
	{
		if (string.IsNullOrWhiteSpace(fileName))
			return false;

		var extension = Path.GetExtension(fileName);
		return string.IsNullOrEmpty(extension) || extension == ".";
	}

	private static bool IsZeroLengthFile(FileSystemInfo entry)
	{
		try
		{
			return entry is FileInfo fileInfo
				? fileInfo.Length == 0
				: new FileInfo(entry.FullName).Length == 0;
		}
		catch
		{
			return false;
		}
	}

	private sealed class BuildState
	{
		private int _rootAccessDenied;
		private int _hadAccessDenied;

		public bool RootAccessDenied => Volatile.Read(ref _rootAccessDenied) == 1;
		public bool HadAccessDenied => Volatile.Read(ref _hadAccessDenied) == 1;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void MarkRootAccessDenied()
		{
			Interlocked.Exchange(ref _rootAccessDenied, 1);
			Interlocked.Exchange(ref _hadAccessDenied, 1);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void MarkAccessDenied()
		{
			Interlocked.Exchange(ref _hadAccessDenied, 1);
		}
	}

	/// <summary>
	/// High-performance comparer for FileSystemInfo entries.
	/// Sorts directories before files, then by name (case-insensitive).
	/// Uses inlined checks to avoid virtual method overhead.
	/// </summary>
	private sealed class FileSystemInfoComparer : IComparer<FileSystemInfo>
	{
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public int Compare(FileSystemInfo? x, FileSystemInfo? y)
		{
			if (ReferenceEquals(x, y)) return 0;
			if (x is null) return -1;
			if (y is null) return 1;

			// Directories first (inlined attribute check)
			bool xIsDir = IsDirectoryFast(x);
			bool yIsDir = IsDirectoryFast(y);

			if (xIsDir != yIsDir)
				return xIsDir ? -1 : 1;

			// Then by name, case-insensitive ordinal (fastest string comparison)
			return string.Compare(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);
		}

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static bool IsDirectoryFast(FileSystemInfo entry)
		{
			try
			{
				return (entry.Attributes & FileAttributes.Directory) != 0;
			}
			catch
			{
				return false;
			}
		}
	}
}
