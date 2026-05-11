namespace ThoughtBuffer.Options;

public sealed class ArtifactStorageOptions
{
    public const string SectionName = "ArtifactStorage";

    public string Provider { get; set; } = "Local";
    public string ContainerName { get; set; } = "thoughtbuffer-artifacts";
    public string ConnectionString { get; set; } = "";
    public string LocalRootPath { get; set; } = "";

    public string GetLocalRootPath(string defaultHostFolder)
    {
        if (!string.IsNullOrWhiteSpace(LocalRootPath))
            return Environment.ExpandEnvironmentVariables(LocalRootPath);

        var baseAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(baseAppData, "ThoughtBuffer", defaultHostFolder, "Artifacts");
    }
}
