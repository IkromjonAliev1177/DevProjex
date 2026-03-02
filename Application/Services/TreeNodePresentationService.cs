namespace DevProjex.Application.Services;

public sealed class TreeNodePresentationService(LocalizationService localization, IIconMapper iconMapper)
{
	public TreeNodeDescriptor Build(FileSystemNode root)
	{
		return BuildNode(root, isRoot: true);
	}

	private TreeNodeDescriptor BuildNode(FileSystemNode node, bool isRoot)
	{
		var displayName = node.IsAccessDenied
			? (isRoot ? localization["Tree.AccessDeniedRoot"] : localization["Tree.AccessDenied"])
			: node.Name;

		var iconKey = iconMapper.GetIconKey(node);

		// Pre-allocate capacity to avoid list resizing
		var children = new List<TreeNodeDescriptor>(node.Children.Count);
		foreach (var child in node.Children)
			children.Add(BuildNode(child, isRoot: false));

		return new TreeNodeDescriptor(
			DisplayName: displayName,
			FullPath: node.FullPath,
			IsDirectory: node.IsDirectory,
			IsAccessDenied: node.IsAccessDenied,
			IconKey: iconKey,
			Children: children);
	}
}
