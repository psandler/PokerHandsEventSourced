namespace PokerEventSourced.Tests.TestSupport;

/// <summary>Finds files in the repo (samples, local dataset) from the test's output folder.</summary>
public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string Samples => Path.Combine(Root, "samples");

    /// <summary>The git-ignored folder that scripts/download-handhq.ps1 fills.</summary>
    public static string LocalDataset => Path.Combine(Root, "_phh-dataset-local");

    public static string ReadSample(string relativePath) => File.ReadAllText(Path.Combine(Samples, relativePath));

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PokerEventSourced.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Can't find the repo root (PokerEventSourced.slnx).");
    }
}
