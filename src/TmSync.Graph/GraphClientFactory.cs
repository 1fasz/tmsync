using Azure.Identity;
using Microsoft.Graph;
using TmSync.Core.Config;

namespace TmSync.Graph;

/// <summary>
/// Builds an app-only GraphServiceClient using the client-credentials flow
/// (Entra ID app registration with application permissions).
/// </summary>
public static class TmGraphClientFactory
{
    public static GraphServiceClient Create(Microsoft365Options options)
    {
        var credential = new ClientSecretCredential(options.TenantId, options.ClientId, options.ClientSecret);
        return new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
    }
}
