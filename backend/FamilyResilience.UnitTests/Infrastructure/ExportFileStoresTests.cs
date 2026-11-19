using FamilyResilience.Infrastructure.Exports;

namespace FamilyResilience.UnitTests.Infrastructure;

public sealed class ExportFileStoresTests
{
    private static readonly string Absolute = Path.Combine(Path.GetTempPath(), "azwah-store-choice");

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    public void Without_a_directory_exports_are_not_configured(string? directory, bool localAllowed) =>
        Assert.IsType<UnconfiguredExportFileStore>(ExportFileStores.Create(directory, localAllowed));

    [Fact]
    public void Outside_development_and_tests_a_local_directory_is_ignored() =>
        Assert.IsType<UnconfiguredExportFileStore>(ExportFileStores.Create(Absolute, localStorageAllowed: false));

    [Fact]
    public void In_development_an_absolute_directory_gives_local_storage() =>
        Assert.IsType<LocalExportFileStore>(ExportFileStores.Create(Absolute, localStorageAllowed: true));

    [Fact]
    public void A_relative_directory_is_refused_so_files_never_land_in_the_working_tree() =>
        Assert.Throws<InvalidOperationException>(() => ExportFileStores.Create("exports", localStorageAllowed: true));
}
