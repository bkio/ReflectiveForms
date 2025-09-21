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
    public required string ReflectiveFormsEndpointPathPrefix = "/rf";

    /// <summary>
    /// The public base URL for accessing ReflectiveForms endpoints. This represents the final, public-facing URL
    /// from which clients (e.g., browsers or external apps) access the endpoints, used for constructing full URLs
    /// such as links in API responses, redirects, or documentation. It may differ from the internal ASP.NET server's
    /// listen URL (e.g., in production behind a reverse proxy, load balancer, or CDN).
    ///
    /// <b>Public URLs (with <see cref="PublicPathForFrontendProxy"/> = "/" and <see cref="PublicPathForApiProxy"/> = "/rf-api/"):</b>
    /// - Frontend pages: {<see cref="PublicBaseUrl"/>}{<see cref="PublicPathForFrontendProxy"/>}entities, {<see cref="PublicBaseUrl"/>}{<see cref="PublicPathForFrontendProxy"/>}entities-admin
    /// - API endpoints: {<see cref="PublicBaseUrl"/>}{<see cref="PublicPathForApiProxy"/>}...
    ///
    /// <b>Internal backend URLs (endpoints registered on ASP.NET, assuming <see cref="ReflectiveFormsEndpointPathPrefix"/> is "/rf"):</b>
    /// - Frontend proxies target: /rf/frontend/entities, /rf/frontend/entities-admin
    /// - API proxies target: /rf/api/...
    ///
    /// <b>Frontend proxy setup examples:</b>
    /// The frontend server examples (React, Angular, etc.) which use simplified public paths, proxying them to the internal backend URLs.
    /// This allows clean public URLs while keeping the backend's prefixed structure.
    ///
    /// <b>React proxy example (create-react-app, setupProxy.js):</b>
    /// <code>
    /// const { createProxyMiddleware } = require('http-proxy-middleware');
    /// module.exports = function(app) {
    ///   app.use('/entities', createProxyMiddleware({ target: 'http://localhost:9000', changeOrigin: true, pathRewrite: { '^/entities': '/rf/frontend/entities' } }));
    ///   app.use('/entities-admin', createProxyMiddleware({ target: 'http://localhost:9000', changeOrigin: true, pathRewrite: { '^/entities-admin': '/rf/frontend/entities-admin' } }));
    ///   app.use('/rf-api', createProxyMiddleware({ target: 'http://localhost:9000', changeOrigin: true, pathRewrite: { '^/rf-api': '/rf/api' } }));
    /// };
    ///
    /// fetch('/entities') // Public: {<see cref="PublicBaseUrl"/>}/entities; Proxied to internal /rf/frontend/entities
    ///   .then(res => res.json())
    ///   .then(data => console.log(data));
    ///
    /// fetch('/entities-admin', { method: 'POST', body: JSON.stringify(payload) }); // Public: {<see cref="PublicBaseUrl"/>}/entities-admin; Proxied to internal /rf/frontend/entities-admin
    ///
    /// fetch('/rf-api/some-endpoint') // Public: {<see cref="PublicBaseUrl"/>}/rf-api/some-endpoint; Proxied to internal /rf/api/some-endpoint
    ///   .then(res => res.json())
    ///   .then(data => console.log(data));
    /// </code>
    ///
    /// <b>Angular proxy example (proxy.conf.json):</b>
    /// <code>
    /// {
    ///   "/entities": { "target": "http://localhost:9000", "secure": false, "changeOrigin": true, "pathRewrite": { "^/entities": "/rf/frontend/entities" } },
    ///   "/entities-admin": { "target": "http://localhost:9000", "secure": false, "changeOrigin": true, "pathRewrite": { "^/entities-admin": "/rf/frontend/entities-admin" } },
    ///   "/rf-api": { "target": "http://localhost:9000", "secure": false, "changeOrigin": true, "pathRewrite": { "^/rf-api": "/rf/api" } }
    /// }
    /// ng serve --proxy-config proxy.conf.json
    ///
    /// this.http.get('/entities').subscribe(data => console.log(data)); // Public: {<see cref="PublicBaseUrl"/>}/entities; Proxied to internal /rf/frontend/entities
    /// this.http.post('/entities-admin', payload).subscribe(result => console.log(result)); // Public: {<see cref="PublicBaseUrl"/>}/entities-admin; Proxied to internal /rf/frontend/entities-admin
    /// this.http.get('/rf-api/some-endpoint').subscribe(data => console.log(data)); // Public: {<see cref="PublicBaseUrl"/>}/rf-api/some-endpoint; Proxied to internal /rf/api/some-endpoint
    /// </code>
    ///
    /// <b>Notes:</b>
    /// - The proxy "target" is the internal URL where the frontend can reach the ASP.NET backend (e.g., 'http://backend-service:9000' in Docker/Kubernetes). In production, this may differ from <see cref="PublicBaseUrl"/>, which is the external-facing URL.
    /// - In production, use "secure": true for HTTPS targets.
    /// - Subpaths are preserved (e.g., /entities/foo → internal /rf/frontend/entities/foo, /rf-api/some-entity → internal /rf/api/some-entity).
    /// - Configure CORS on the ASP.NET backend to allow origins from the frontend's public domain.
    ///
    /// Ensure <see cref="PublicBaseUrl"/> accurately reflects the public access point for clients.
    /// </summary>
    public required string PublicBaseUrl { get; init; } = "http://localhost:9000";

    /// <summary>
    /// The public-facing path prefix for frontend routes (e.g., /entities, /entities-admin).
    /// Defaults to "/", meaning frontend endpoints are served directly under <see cref="PublicBaseUrl"/> (e.g., /entities).
    /// Set to a different value (e.g., "/app/") to serve frontend endpoints under a subpath.
    /// </summary>
    public required string PublicPathForFrontendProxy { get; init; } = "/";

    /// <summary>
    /// The public-facing path prefix for API routes.
    /// Defaults to "/rf-api/", meaning API endpoints are served under <see cref="PublicBaseUrl"/>}/rf-api/...
    /// Set to a different value (e.g., "/api/") to serve API endpoints under a custom public path.
    /// </summary>
    public required string PublicPathForApiProxy { get; init; } = "/rf-api/";

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

    internal string FinalPublicUrlBaseForApi => PublicBaseUrl + PublicPathForApiProxy;
    private string FinalPublicUrlBaseForFrontend => PublicBaseUrl + PublicPathForFrontendProxy;

    internal string FinalEntitiesAdminBaseRoute => FinalPublicUrlBaseForFrontend + RfEndpointMapper.EntitiesAdminEndpoint;
    internal string FinalEntitiesBaseRoute => FinalPublicUrlBaseForFrontend + RfEndpointMapper.EntitiesEndpoint;
}
