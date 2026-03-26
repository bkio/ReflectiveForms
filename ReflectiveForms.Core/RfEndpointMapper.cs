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
    internal const string CaptchaChallengeEndpoint = "captcha-challenge";
    internal const string SchemaEndpoint = "schema";

    public static string PublicCrudEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + CrudEndpoint;
    public static string PublicSanityCheckEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + SanityCheckEndpoint;
    public static string PublicEntityLockControlEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + EntityLockControlEndpoint;
    public static string PublicLoginEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + LoginEndpoint;
    public static string PublicLogoutEndpoint => RfConfiguration.EndpointConfiguration.PublicUrlRootForApi + LogoutEndpoint;
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
        MapEndpoint(group, CaptchaChallengeEndpoint, new CaptchaChallenge());

        return app;
    }

    public static WebApplication BuildWithReflectiveFields(
        this WebApplicationBuilder webAppBuilder,
        RfConfigurationBuilder reflectiveFormsBuilder)
    {
        var initializeResult = RfConfiguration.Initialize(reflectiveFormsBuilder);
        if (!initializeResult.IsSuccessful)
            throw new Exception(initializeResult.ErrorMessage);

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

        var app = webAppBuilder.Build();

        app.UseRouting();
        app.UseSession();

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
