using FamilyResilience.Application.Analytics;
using FamilyResilience.Infrastructure.Exports;

namespace FamilyResilience.Api.Admin;

public static class ExportStorageSetup
{
    /// <summary>
    /// Local export storage only in Development and in the test host (D-60): elsewhere exports stay unconfigured until
    /// approved private storage exists (open question 6). Resolved when first used, so a test host's settings apply.
    /// </summary>
    public static IServiceCollection AddExportStorage(this IServiceCollection services) =>
        services.AddSingleton<IExportFileStore>(provider =>
        {
            var environment = provider.GetRequiredService<IHostEnvironment>();
            return ExportFileStores.Create(
                provider.GetRequiredService<IConfiguration>()["Exports:LocalDirectory"],
                localStorageAllowed: environment.IsDevelopment() || environment.IsEnvironment("Testing"));
        });
}
