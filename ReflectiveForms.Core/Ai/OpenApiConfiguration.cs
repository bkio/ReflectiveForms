// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Ai;

/// <summary>
/// Configuration for auto-generated OpenAPI 3.1 spec at /rf/api/openapi.json.
/// Independent of AI services — only requires this config to be non-null on <see cref="Endpoints.EndpointConfiguration"/>.
/// </summary>
public sealed class OpenApiConfiguration
{
    /// <summary>
    /// API title in the OpenAPI info block.
    /// </summary>
    public string Title { get; init; } = "ReflectiveForms API";

    /// <summary>
    /// API version string.
    /// </summary>
    public string Version { get; init; } = "1.0.0";

    /// <summary>
    /// Optional description for the API.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Optional contact email.
    /// </summary>
    public string? ContactEmail { get; init; }

    /// <summary>
    /// Include auth endpoints (login, logout, auth_check) in the spec.
    /// </summary>
    public bool IncludeAuthEndpoints { get; init; } = true;

    /// <summary>
    /// Include schema endpoints in the spec.
    /// </summary>
    public bool IncludeSchemaEndpoints { get; init; } = true;

    /// <summary>
    /// Include media endpoints in the spec.
    /// </summary>
    public bool IncludeMediaEndpoints { get; init; } = true;

    /// <summary>
    /// Include ReflectiveForms extension properties (x-rf-*) in field schemas.
    /// These carry display_condition, dynamic choices info, etc. — useful as AI context.
    /// </summary>
    public bool IncludeRfExtensions { get; init; } = true;

    /// <summary>
    /// Include AI-specific endpoints (semantic search, generate, etc.) in the spec.
    /// Only relevant when <see cref="AiServiceConfiguration"/> is also set.
    /// </summary>
    public bool IncludeAiEndpoints { get; init; } = true;

    /// <summary>
    /// When true, the OpenAPI endpoint requires authentication (JwtOrCookie).
    /// When false (default), the endpoint is public — same as /schema.
    /// Set to true for deployments where API structure should not be publicly visible.
    /// </summary>
    public bool RequireAuthentication { get; init; }
}
