namespace FamilyResilience.UnitTests.Support;

/// <summary>Files of the repository itself (sources, docs), found from the test output folder.</summary>
internal static class RepositoryFiles
{
    public static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyResilience.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.Parent?.FullName
            ?? throw new InvalidOperationException("FamilyResilience.slnx was not found above the test output folder.");
    }

    public static string ReadText(string relativePath) => File.ReadAllText(Path.Combine(Root(), relativePath));
}
