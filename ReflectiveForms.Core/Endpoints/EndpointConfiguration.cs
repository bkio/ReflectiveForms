// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Text;
using CrossCloudKit.Utilities.Common;
using Microsoft.IdentityModel.Tokens;

namespace ReflectiveForms.Core.Endpoints;

// ReSharper disable once ClassNeverInstantiated.Global
public sealed class EndpointConfiguration
{
    /// <summary>
    /// The URL path prefix for all ReflectiveForms API and frontend endpoints on the ASP.NET backend.
    /// Defaults to "/rf", hosting endpoints under "/rf/...".
    ///
    /// Best practice: Keep the prefix short and consistent across your application.
    /// </summary>
    public required string RootPath = "/rf";

    /// <summary>
    /// The public-facing url path root for frontend routes (e.g., /entities, /entities-admin).
    /// As we are generating frontend code, we need to know the public url of endpoints.
    /// Typically, this application is served behind a reverse proxy.
    /// <b>This public url must be mapped (via reverse proxy) to:</b>
    /// <b>{{<see cref="RootPath"/>}}/frontend</b>
    /// </summary>
    public required string PublicUrlRootForFrontend { get; init; } = "http://localhost:9000/rf/frontend/";

    /// <summary>
    /// The public-facing url path root for API routes.
    /// As we are generating frontend code, we need to know the public url of endpoints.
    /// Typically, this application is served behind a reverse proxy.
    /// <b>This public url must be mapped (via reverse proxy) to:</b>
    /// <b>{{<see cref="RootPath"/>}}/api</b>
    /// </summary>
    public required string PublicUrlRootForApi { get; init; } = "http://localhost:9000/rf/api/";

    /// <summary>
    /// The secret key used for signing and validating JSON Web Tokens (JWT) within the application.
    /// This key is critical for ensuring the integrity and authenticity of issued tokens.
    /// Best practice: Use a secure, randomly generated string and protect it from unauthorized access.
    /// Changing this value will invalidate all existing tokens.
    /// </summary>
    public required string JwtSecret
    {
        init
        {
            JwtSecurityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(value));
            JwtSigningCredentials = new SigningCredentials(JwtSecurityKey, SecurityAlgorithms.HmacSha256);
            var hashed = CryptographyUtilities.CalculateStringSha256(value)[..16];
            JwtIssuer = $"reflective-forms-{hashed}";
            JwtAudience = $"{JwtIssuer}-api-frontend-audience";
            AuthCookieName = $"rf-auth-token-{hashed}";
        }
    }

    internal readonly SymmetricSecurityKey? JwtSecurityKey;
    internal readonly SigningCredentials? JwtSigningCredentials;
    internal readonly string? JwtIssuer;
    internal readonly string? JwtAudience;
    internal readonly string? AuthCookieName;

    internal string FinalEntitiesAdminBaseRoute => PublicUrlRootForFrontend + RfEndpointMapper.EntitiesAdminEndpoint;
    internal string FinalEntitiesBaseRoute => PublicUrlRootForFrontend + RfEndpointMapper.EntitiesEndpoint;
}
