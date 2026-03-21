namespace DevProjex.Tests.UI;

public static class UiWorkspaceCollection
{
    public const string Name = "UI Workspace";
}

[CollectionDefinition(UiWorkspaceCollection.Name)]
public sealed class UiWorkspaceCollectionDefinition : ICollectionFixture<UiWorkspaceFixture>;

public sealed class UiWorkspaceFixture : IDisposable
{
    internal UiTestProject Project { get; } = UiTestProject.CreateDefault();

    public void Dispose()
    {
        Project.Dispose();
    }
}
