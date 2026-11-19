using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyResilience.IntegrationTests.Support;

public static class TestServices
{
    public static void Configure(IServiceCollection services)
    {
        services.AddControllers().AddApplicationPart(typeof(TestOnlyController).Assembly);
        services.AddScoped<IValidator<EchoRequest>, EchoRequestValidator>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestData.Now));

        // Registered after the API's own call, so the test scheme becomes the default.
        services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
    }
}
