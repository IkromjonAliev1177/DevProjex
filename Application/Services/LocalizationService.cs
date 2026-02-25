namespace DevProjex.Application.Services;

public sealed class LocalizationService(ILocalizationCatalog catalog, AppLanguage initialLanguage)
{
	public AppLanguage CurrentLanguage { get; private set; } = initialLanguage;

	public event EventHandler? LanguageChanged;

	public string this[string key]
	{
		get
		{
			var dict = catalog.Get(CurrentLanguage);
			return dict.TryGetValue(key, out var value) ? value : $"[[{key}]]";
		}
	}

	public string Format(string key, params object[] args) => string.Format(this[key], args);

	public void SetLanguage(AppLanguage language)
	{
		if (CurrentLanguage == language) return;

		CurrentLanguage = language;
		LanguageChanged?.Invoke(this, EventArgs.Empty);
	}
}
