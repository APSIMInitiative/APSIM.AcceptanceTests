namespace AcceptanceTestsWebAPI.Services;

/// <summary>Configures the Azure storage account and persistent local database directory.</summary>
public sealed class DatabaseFetchOptions
{
    /// <summary>HTTPS URI of the Azure storage account, without credentials.</summary>
    public string StorageAccountUri { get; set; } = string.Empty;
    /// <summary>Download root; relative paths are resolved against the API content root.</summary>
    public string Directory { get; set; } = string.Empty;
}
