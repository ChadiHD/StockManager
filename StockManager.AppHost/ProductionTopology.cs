#pragma warning disable ASPIREACADOMAINS001, ASPIREPROBES001, AZPROVISION001

using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.KeyVault;
using Microsoft.Extensions.Configuration;

/// <summary>
/// What a published deployment adds to the graph: the Azure resources local runs do without, and
/// the settings only a container platform needs (T7).
/// </summary>
/// <remarks>
/// Publish mode only. Every resource here would need an Azure subscription to start, and local
/// runs, the E2E suite and CI must not. Nothing in this file deploys anything: <c>aspire
/// publish</c> writes Bicep, and a person runs the deployment (T7 plan, D1).
///
/// - **Key Vault** holds the JWT signing key and the admin bootstrap password, so neither is a
///   plain container-app setting, and the key that wraps the Data Protection ring. Purge
///   protection is on because that key cannot be lost: every cookie, stored feed credential and
///   queued reset link depends on it. The key itself is created by the runbook, once.
/// - **Application Insights** receives every host's telemetry; ServiceDefaults turns the
///   exporter on when the connection string is present.
/// - **Forwarded headers** on both hosts, because the ingress terminates TLS: without them
///   UseHttpsRedirection loops, and every customer shares the ingress's address in the per-IP
///   rate limits.
/// - **Probes** on /alive and /health, which ServiceDefaults maps in every environment, on the
///   http endpoint: the ingress terminates TLS, so that is what the app listens on there.
/// - **One replica, never zero**, for both. StockApi runs five scheduled loops that do not run in
///   no replica (T7 plan, D5); the storefront keeps the shop window warm.
/// - **Custom domains** from <c>Deployment:StoreDomains</c> and <c>Deployment:AdminDomain</c>,
///   set by the deploy workflow, each with a managed-certificate name supplied at deploy. The
///   first deploy has no certificate; the runbook covers binding one.
/// - **StockApi's own settings** from <c>Deployment:StockApi</c>, also set by the workflow:
///   <c>Deployment__StockApi__Feeds__SyncEnabled</c> becomes <c>Feeds__SyncEnabled</c> on the
///   container app.
/// - **Mail** through Communication Services at <c>acs-endpoint</c>. The resource and its sender
///   domains are created and verified by hand — domain verification is DNS work — so it is a
///   parameter here rather than a resource.
/// </remarks>
internal static class ProductionTopology
{
    public static void Apply(
        IDistributedApplicationBuilder builder,
        IResourceBuilder<ProjectResource> api,
        IResourceBuilder<ProjectResource> store,
        IResourceBuilder<ParameterResource> jwtSigningKey)
    {
        var vault = builder.AddAzureKeyVault("vault")
            .ConfigureInfrastructure(infrastructure =>
            {
                var keyVault = infrastructure.GetProvisionableResources().OfType<KeyVaultService>().Single();
                keyVault.Properties.EnablePurgeProtection = true;
            });

        var insights = builder.AddAzureApplicationInsights("insights");

        var jwt = vault.AddSecret("jwt-signing-key-secret", "jwt-signing-key", jwtSigningKey);
        var bootstrapPassword = vault.AddSecret(
            "admin-bootstrap-password-secret", "admin-bootstrap-password", builder.AddParameter("admin-bootstrap-password", secret: true));
        var bootstrapEmail = builder.AddParameter("admin-bootstrap-email");
        var dataProtectionKey = builder.AddParameter("dataprotection-key-uri");
        var acsEndpoint = builder.AddParameter("acs-endpoint");

        foreach (var app in new[] { api, store })
        {
            app.WithReference(insights)
                .WithEnvironment("ASPNETCORE_FORWARDEDHEADERS_ENABLED", "true")
                .WithEnvironment("DataProtection__KeyVaultKeyUri", dataProtectionKey)
                .WithHttpProbe(ProbeType.Liveness, "/alive", endpointName: "http")
                .WithHttpProbe(ProbeType.Readiness, "/health", endpointName: "http");
        }

        api.WithRoleAssignments(vault, KeyVaultBuiltInRole.KeyVaultCryptoUser, KeyVaultBuiltInRole.KeyVaultSecretsUser)
            .WithEnvironment("Jwt__SigningKey", jwt.Resource)
            .WithEnvironment("Admin__BootstrapEmail", bootstrapEmail)
            .WithEnvironment("Admin__BootstrapPassword", bootstrapPassword.Resource)
            .WithEnvironment("Email__Transport", "Acs")
            .WithEnvironment("Email__AcsEndpoint", acsEndpoint);

        // A deployment is where StockApi's off-by-default work is turned on: the nightly feed sync
        // and the quote-expiry reminders. Without this, nothing could set them short of editing
        // shared code. Not for secrets: these land in the container app's plain environment.
        foreach (var (key, value) in builder.Configuration.GetSection("Deployment:StockApi").AsEnumerable(makePathsRelative: true))
        {
            if (value is not null)
            {
                api.WithEnvironment(key.Replace(":", "__"), value);
            }
        }

        store.WithRoleAssignments(vault, KeyVaultBuiltInRole.KeyVaultCryptoUser);

        var adminDomains = Domains(builder, "admin", builder.Configuration["Deployment:AdminDomain"] is { Length: > 0 } admin ? [admin] : []);
        var storeDomains = Domains(builder, "store", builder.Configuration.GetSection("Deployment:StoreDomains").Get<string[]>() ?? []);

        api.PublishAsAzureContainerApp((_, app) => Configure(app, adminDomains));
        store.PublishAsAzureContainerApp((_, app) => Configure(app, storeDomains));
    }

    private static List<(IResourceBuilder<ParameterResource> Domain, IResourceBuilder<ParameterResource> Certificate)> Domains(
        IDistributedApplicationBuilder builder, string prefix, IEnumerable<string> hosts) =>
        hosts.Select((host, index) => (
                builder.AddParameter($"{prefix}-domain-{index}", host, publishValueAsDefault: true),
                builder.AddParameter($"{prefix}-certificate-{index}")))
            .ToList();

    private static void Configure(
        ContainerApp app,
        List<(IResourceBuilder<ParameterResource> Domain, IResourceBuilder<ParameterResource> Certificate)> domains)
    {
        app.Template.Scale = new ContainerAppScale { MinReplicas = 1, MaxReplicas = 10 };

        foreach (var (domain, certificate) in domains)
        {
            app.ConfigureCustomDomain(domain, certificate);
        }
    }
}
