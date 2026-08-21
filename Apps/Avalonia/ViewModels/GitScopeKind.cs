namespace DevProjex.Avalonia.ViewModels;

public enum GitScopeKind
{
	AllFiles,
	Tracked,
	Changed,
	Staged,
	BranchDiff
}

public sealed record GitScopeOptionViewModel(
	GitScopeKind Kind,
	string Label,
	string Tooltip);
