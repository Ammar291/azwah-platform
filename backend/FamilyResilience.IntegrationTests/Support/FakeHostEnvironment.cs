using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace FamilyResilience.IntegrationTests.Support;

internal sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = environmentName;

    public string ApplicationName { get; set; } = "FamilyResilience.IntegrationTests";

    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
