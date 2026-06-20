// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Interfaces.Classes.Asp;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using ReflectiveForms.Core.Endpoints;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Endpoints.Mapped.Api;
// ReSharper disable MemberCanBePrivate.Global

namespace ReflectiveForms.Core;

public static class RfEndpointMapper
{
    private const string ApiRouteSegment = "/api/";

    //Api
    internal const string CrudEndpoint = "crud";
    internal const string SanityCheckEndpoint = "sanity_check";
    internal const string EntityLockControlEndpoint = "entity_lock_control";
    internal const string MediaEndpoint = "media";
    internal const string LoginEndpoint = "login";
    internal const string LogoutEndpoint = "logout";
    internal const string AuthCheckEndpoint = "auth_check";
    internal const string CapabilitiesEndpoint = "capabilities";
    internal const string SchemaEndpoint = "schema";
    internal const string BulkReadEndpoint = "bulk_read";
    internal const string FrontendSettingsEndpoint = "frontend_settings";
    internal const string LiveEndpointPattern = "live/{entityName}/{entityId}";

    public static string PublicCrudEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + CrudEndpoint;
    public static string PublicSanityCheckEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + SanityCheckEndpoint;
    public static string PublicEntityLockControlEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + EntityLockControlEndpoint;
    public static string PublicLoginEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + LoginEndpoint;
    public static string PublicLogoutEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + LogoutEndpoint;
    public static string PublicAuthCheckEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + AuthCheckEndpoint;
    public static string PublicCapabilitiesEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + CapabilitiesEndpoint;
    public static string PublicSchemaEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + SchemaEndpoint;

    private static WebApplication MapEndpoints(WebApplication app)
    {
        var group = app.MapGroup(RfConfiguration.EndpointConfiguration.RootPath);

        MapEndpoint(group, CrudEndpoint, new Crud());
        MapEndpoint(group, SanityCheckEndpoint, new SanityCheck());
        MapEndpoint(group, EntityLockControlEndpoint, new EntityLockControl());
        MapEndpoint(group, MediaEndpoint, new Media());
        MapEndpoint(group, SchemaEndpoint, new SchemaEndpoint());
        MapEndpoint(group, LoginEndpoint, new Login());
        MapEndpoint(group, LogoutEndpoint, new Logout());
        MapEndpoint(group, AuthCheckEndpoint, new AuthCheck());
        MapEndpoint(group, CapabilitiesEndpoint, new Capabilities());
        MapEndpoint(group, BulkReadEndpoint, new BulkRead());
        MapEndpoint(group, FrontendSettingsEndpoint, new FrontendSettings());

        // OpenAPI spec — independent of AI
        if (RfConfiguration.EndpointConfiguration.OpenApi != null)
        {
            MapEndpoint(group, "openapi.json", new OpenApiEndpoint());
        }

        // AI endpoints — only registered if AiServiceConfiguration is set
        if (RfConfiguration.AiServiceConfiguration != null)
        {
            MapEndpoint(group, "ai/semantic_search", new AiSemanticSearchEndpoint());
            MapEndpoint(group, "ai/reindex", new AiReIndexEndpoint());
            MapEndpoint(group, "ai/generate", new AiGenerateEndpoint());
            MapEndpoint(group, "ai/suggest", new AiSuggestFieldEndpoint());
            MapEndpoint(group, "ai/sanity_check", new AiSanityCheckEndpoint());
            MapEndpoint(group, "ai/diff_summary", new AiDiffSummaryEndpoint());
            MapEndpoint(group, "ai/nl_filter", new AiNaturalLanguageFilterEndpoint());
            MapEndpoint(group, "ai/relation_suggest", new AiRelationSuggestEndpoint());
            MapEndpoint(group, "ai/chat", new AiAgentChatEndpoint());
        }

        // WebSocket endpoint for live entity updates (editor → viewers relay)
        group.Map(ApiRouteSegment + LiveEndpointPattern, LiveUpdateWebSocket.HandleAsync)
            .RequireAuthorization("JwtOrCookie");

        return app;
    }

    public static WebApplication BuildWithReflectiveFields(
        this WebApplicationBuilder webAppBuilder,
        RfConfigurationBuilder reflectiveFormsBuilder)
    {
        var initializeResult = RfConfiguration.Initialize(reflectiveFormsBuilder);
        if (!initializeResult.IsSuccessful)
            throw new Exception(initializeResult.ErrorMessage);

        // Configure graceful shutdown so in-flight requests complete before
        // Kestrel stops — reduces CORS policy execution failure noise during restarts.
        webAppBuilder.Services.Configure<HostOptions>(options =>
        {
            options.ShutdownTimeout = TimeSpan.FromSeconds(30);
        });

        webAppBuilder.Services.AddAuthentication(options =>
        {
            // Don't set a single default scheme - let both JWT and Cookie schemes work
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = RfConfiguration.EndpointConfiguration.JwtIssuer,
                ValidAudience = RfConfiguration.EndpointConfiguration.JwtAudience,
                IssuerSigningKey = RfConfiguration.EndpointConfiguration.JwtSecurityKey
            };
        })
        .AddCookie(CookieAuthenticationDefaults.AuthenticationScheme, options =>
        {
            options.Cookie.Name = RfConfiguration.EndpointConfiguration.AuthCookieName;
            options.Cookie.HttpOnly = true;      // prevent JS access
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; //Based on if request is https or http
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(JwtAndCookieManager.TokenExpirationMinutes);

            // redirect logic (disable for APIs)
            options.Events.OnRedirectToLogin = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            };
            options.Events.OnRedirectToAccessDenied = ctx =>
            {
                ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            };
        });

        // Add session support for CAPTCHA
        webAppBuilder.Services.AddSingleton<IDistributedCache>(
            new MemoryServiceDistributedCache(
                RfConfiguration.RepositoryService.MemoryServiceInstance,
                new MemoryScopeLambda($"cache-{RfConfiguration.EndpointConfiguration.JwtIssuer.NotNull()}")));

        webAppBuilder.Services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(10);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        webAppBuilder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("JwtOrCookie", policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme);
            });
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddAuthenticationSchemes(JwtBearerDefaults.AuthenticationScheme, CookieAuthenticationDefaults.AuthenticationScheme)
                .Build();
        });

        // Built-in CORS policy using the configured frontend URL
        webAppBuilder.Services.AddCors(options =>
        {
            options.AddPolicy("ReflectiveFormsCors", policy =>
            {
                policy.WithOrigins(
                        RfConfiguration.EndpointConfiguration.PublicFrontendBaseUrl.TrimEnd('/'))
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .AllowCredentials();
            });
        });

        var app = webAppBuilder.Build();

        app.UseRouting();

        // CORS must be between UseRouting and UseAuthentication
        // to handle preflight (OPTIONS) requests correctly
        app.UseCors("ReflectiveFormsCors");

        app.UseSession();

        app.UseWebSockets(new Microsoft.AspNetCore.Builder.WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
        });

        app.UseAuthentication();
        app.UseAuthorization();

        return MapEndpoints(app);
    }

    private static void MapEndpoint(
        RouteGroupBuilder builder,
        string endpoint,
        BaseEndpoint handler)
    {
        var pattern = ApiRouteSegment + endpoint;

        var mapped = builder.MapMethods(
            pattern,
            handler.AllowedMethods().Select(m => m.ToHttpMethodString()),
            handler.InvokeAsync
        );
        if (handler.IsAuthenticatedEndpoint())
            mapped.RequireAuthorization("JwtOrCookie");
    }
}
