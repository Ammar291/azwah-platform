using Microsoft.Extensions.Options;

namespace FamilyResilience.IdentityProvider;

/// <summary>Says at start-up which mode the provider runs in, and names confidential clients that have no secret yet.</summary>
internal sealed class StartupAnnouncement(
    IOptions<IdentityProviderOptions> options,
    ClientRegistry clients,
    ProviderMode mode,
    ILoggerFactory loggers) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var logger = IdentityProviderLog.For(loggers);
        if (mode.IsDeployed)
        {
            IdentityProviderLog.StartedDeployed(logger, options.Value.Issuer);
        }
        else
        {
            IdentityProviderLog.Started(logger, options.Value.Issuer);
        }

        foreach (var client in clients.All.Where(client => client is { Confidential: true, Secret: null }))
        {
            IdentityProviderLog.ClientWithoutSecret(logger, client.Id);
        }

        if (options.Value.UaePass.IsActive)
        {
            IdentityProviderLog.UaePassEnabled(logger, UaePassOptions.CallbackUri(options.Value.Issuer));
        }
        else if (options.Value.UaePass.Enabled)
        {
            IdentityProviderLog.UaePassWithoutCredentials(logger);
        }

        if (options.Value.Google.IsActive)
        {
            IdentityProviderLog.GoogleEnabled(logger, GoogleOptions.CallbackUri(options.Value.Issuer));
        }
        else if (options.Value.Google.Enabled)
        {
            IdentityProviderLog.GoogleWithoutCredentials(logger);
        }

        if (options.Value.Google.IsActive)
        {
            foreach (var client in clients.All)
            {
                if (options.Value.Google.StaffRolesFor(client, mode) is { } staffRoles)
                {
                    IdentityProviderLog.GoogleStaffSignInEnabled(logger, client.Id, string.Join(", ", staffRoles));
                }
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
