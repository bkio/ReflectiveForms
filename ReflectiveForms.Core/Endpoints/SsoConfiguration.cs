// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Endpoints;

/// <summary>
/// Supported SSO identity providers.
/// </summary>
public enum SsoProvider
{
    OpenIdConnect,
    AzureAd,
    Google
}

/// <summary>
/// Defines how IdP claims are mapped to ReflectiveForms user fields.
/// </summary>
public sealed class ClaimsMappings
{
    /// <summary>
    /// The claim type that contains the user's email address.
    /// Default: "email"
    /// </summary>
    public string Email { get; init; } = "email";

    /// <summary>
    /// The claim type that contains the user's display name.
    /// Default: "name"
    /// </summary>
    public string Name { get; init; } = "name";
}

/// <summary>
/// Configuration for Single Sign-On (SSO) via OpenID Connect.
/// When set on <see cref="EndpointConfiguration.SsoConfiguration"/>,
/// the backend registers OIDC authentication and exposes SSO login/callback endpoints.
/// </summary>
public sealed class SsoConfiguration
{
    /// <summary>
    /// The SSO identity provider type.
    /// </summary>
    public required SsoProvider Provider { get; init; }

    /// <summary>
    /// The OIDC authority URL (e.g. "https://login.microsoftonline.com/{tenant-id}/v2.0").
    /// </summary>
    public required string Authority { get; init; }

    /// <summary>
    /// The OAuth2 client ID registered with the identity provider.
    /// </summary>
    public required string ClientId { get; init; }

    /// <summary>
    /// The OAuth2 client secret registered with the identity provider.
    /// </summary>
    public required string ClientSecret { get; init; }

    /// <summary>
    /// The callback path on the backend that the IdP redirects to after authentication.
    /// Default: "/auth/sso/callback"
    /// </summary>
    public string CallbackPath { get; init; } = "/auth/sso/callback";

    /// <summary>
    /// How IdP claims map to ReflectiveForms user fields.
    /// </summary>
    public ClaimsMappings ClaimsMappings { get; init; } = new();

    /// <summary>
    /// Whether to automatically create a ReflectiveForms user when a new SSO user logs in.
    /// When false, an admin must manually create a user with a matching email first.
    /// Default: true
    /// </summary>
    public bool AutoProvisionUsers { get; init; } = true;

    /// <summary>
    /// The default role name assigned to auto-provisioned SSO users.
    /// Default: "editor"
    /// </summary>
    public string DefaultRole { get; init; } = "editor";

    /// <summary>
    /// Restrict SSO login to users with email addresses in these domains.
    /// Empty means all domains are allowed.
    /// Example: ["school.edu", "university.org"]
    /// </summary>
    public string[] AllowedDomains { get; init; } = [];
}
