using Avalonia.Controls;

namespace DevProjex.Tests.Unit.Avalonia;

public sealed class TreeSearchCoordinatorTests
{
	[Fact]
	public void UpdateSearchMatches_EmptyQuery_CollapsesDescendantsAndClearsMatches()
	{
		var (viewModel, treeView) = CreateContext();
		var root = CreateTree();
		viewModel.TreeNodes.Add(root);

		root.IsExpanded = true;
		root.Children[1].IsExpanded = true;

		using var coordinator = new TreeSearchCoordinator(viewModel, treeView);
		viewModel.SearchQuery = string.Empty;

		coordinator.UpdateSearchMatches();

		Assert.False(coordinator.HasMatches);
		Assert.False(root.Children[1].IsExpanded);
		Assert.False(root.Children[1].Children[0].IsExpanded);
	}

	[Fact]
	public void UpdateSearchMatches_WithSingleDeepMatch_SelectsNodeAndExpandsAncestors()
	{
		var (viewModel, treeView) = CreateContext();
		var root = CreateTree();
		viewModel.TreeNodes.Add(root);

		using var coordinator = new TreeSearchCoordinator(viewModel, treeView);
		viewModel.SearchQuery = "delta";

		coordinator.UpdateSearchMatches();

		var delta = root.Children[1].Children[0];
		Assert.True(coordinator.HasMatches);
		Assert.True(root.IsExpanded);
		Assert.True(root.Children[1].IsExpanded);
		Assert.Same(delta, treeView.SelectedItem);
		Assert.True(delta.IsSelected);
		Assert.True(delta.IsCurrentSearchMatch);
	}

	[Fact]
	public void Navigate_CyclesForwardAndBackwardAcrossMatches()
	{
		var (viewModel, treeView) = CreateContext();
		var root = CreateTree();
		viewModel.TreeNodes.Add(root);

		using var coordinator = new TreeSearchCoordinator(viewModel, treeView);
		viewModel.SearchQuery = "ta";

		coordinator.UpdateSearchMatches();
		var beta = root.Children[1];
		var delta = root.Children[1].Children[0];

		Assert.Same(beta, treeView.SelectedItem);

		coordinator.Navigate(1);
		Assert.Same(delta, treeView.SelectedItem);

		coordinator.Navigate(1);
		Assert.Same(beta, treeView.SelectedItem);

		coordinator.Navigate(-1);
		Assert.Same(delta, treeView.SelectedItem);
	}

	[Fact]
	public void ClearSearchState_RemovesCurrentMatchAndHighlights()
	{
		var (viewModel, treeView) = CreateContext();
		var root = CreateTree();
		viewModel.TreeNodes.Add(root);

		using var coordinator = new TreeSearchCoordinator(viewModel, treeView);
		viewModel.SearchQuery = "delta";
		coordinator.UpdateSearchMatches();

		var delta = root.Children[1].Children[0];
		Assert.True(delta.IsCurrentSearchMatch);
		Assert.True(delta.HasHighlightedDisplay);

		coordinator.ClearSearchState();

		Assert.False(coordinator.HasMatches);
		Assert.False(delta.IsCurrentSearchMatch);
		Assert.False(delta.HasHighlightedDisplay);
	}

	private static (MainWindowViewModel viewModel, TreeView treeView) CreateContext()
	{
		var catalog = new StubLocalizationCatalog(new Dictionary<AppLanguage, IReadOnlyDictionary<string, string>>
		{
			[AppLanguage.En] = new Dictionary<string, string>()
		});
		var localization = new LocalizationService(catalog, AppLanguage.En);
		var viewModel = new MainWindowViewModel(localization, new HelpContentProvider());
		var treeView = new TreeView();
		return (viewModel, treeView);
	}

	private static TreeNodeViewModel CreateTree()
	{
		var root = new TreeNodeViewModel(CreateDescriptor("Root"), null, null);
		var alpha = new TreeNodeViewModel(CreateDescriptor("Alpha"), root, null);
		var beta = new TreeNodeViewModel(CreateDescriptor("Beta"), root, null);
		var delta = new TreeNodeViewModel(CreateDescriptor("Delta"), beta, null);

		beta.Children.Add(delta);
		root.Children.Add(alpha);
		root.Children.Add(beta);
		return root;
	}

	private static TreeNodeDescriptor CreateDescriptor(string name, params TreeNodeDescriptor[] children)
	{
		return new TreeNodeDescriptor(
			DisplayName: name,
			FullPath: $"C:\\{name}",
			IsDirectory: true,
			IsAccessDenied: false,
			IconKey: "icon",
			Children: children);
	}
}
