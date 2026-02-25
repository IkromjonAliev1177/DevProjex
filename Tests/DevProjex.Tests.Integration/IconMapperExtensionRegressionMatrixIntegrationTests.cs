namespace DevProjex.Tests.Integration;

public sealed class IconMapperExtensionRegressionMatrixIntegrationTests
{
	public static IEnumerable<object[]> Cases()
	{
		yield return ["main.ts", "typescript"];
		yield return ["main.tsx", "react"];
		yield return ["Program.cs", "csharp"];
		yield return ["main.rs", "rust"];
		yield return ["index.js", "js"];
		yield return ["package.json", "json"];
		yield return ["app.py", "python"];
		yield return ["README.md", "md"];
		yield return ["go.mod", "unknownFile"];
		yield return ["Dockerfile", "docker"];
		yield return ["archive.zip", "7zip"];
		yield return ["movie.mp4", "video"];
		yield return ["song.mp3", "audio"];
		yield return ["diagram.svg", "picture"];
		yield return ["query.sql", "sql"];
		yield return ["config.yaml", "yaml"];
		yield return ["config.yml", "yml"];
		yield return ["settings.toml", "conf"];
		yield return ["build.ps1", "powershell"];
		yield return ["run.bat", "batch"];
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void IconMapper_MapsCriticalExtensions_RegressionMatrix(string fileName, string expectedIconKey)
	{
		var mapper = new IconMapper();
		var node = new FileSystemNode(fileName, "/repo/" + fileName, isDirectory: false, isAccessDenied: false, new List<FileSystemNode>());

		var actual = mapper.GetIconKey(node);

		Assert.Equal(expectedIconKey, actual);
	}
}
