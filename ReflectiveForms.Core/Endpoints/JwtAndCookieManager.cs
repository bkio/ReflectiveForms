// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints;

internal static class JwtAndCookieManager
{
    private static readonly JwtSecurityTokenHandler JwtSecurityTokenHandler = new();

    public const int TokenExpirationMinutes = 30;

    private static Claim[] BuildJwtClaims(EntityModel<UserEntityFieldsModel> user)
    {
        var userFields = user.Fields;

        return
        [
            new Claim(JwtRegisteredClaimNames.Sub, $"{user.Id}"),
            new Claim(JwtRegisteredClaimNames.Email, $"{userFields.EmailAddress}"),
            new Claim(JwtRegisteredClaimNames.EmailVerified, "false"),
            new Claim(JwtRegisteredClaimNames.Name, $"{user.Title.Text}"),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        ];
    }
    private static Claim[] BuildCookieClaims(EntityModel<UserEntityFieldsModel> user)
    {
        var userFields = user.Fields;

        return
        [
            new Claim(ClaimTypes.NameIdentifier, $"{user.Id}"),
            new Claim(ClaimTypes.Email, userFields.EmailAddress),
            new Claim(ClaimTypes.Name, user.Title.Text)
        ];
    }

    public static string GenerateJwtToken(EntityModel<UserEntityFieldsModel> user)
    {
        return JwtSecurityTokenHandler.WriteToken(new JwtSecurityToken(
            issuer: RfConfiguration.EndpointConfiguration.JwtIssuer,
            audience: RfConfiguration.EndpointConfiguration.JwtAudience,
            claims: BuildJwtClaims(user),
            expires: DateTime.UtcNow.AddMinutes(TokenExpirationMinutes),
            signingCredentials: RfConfiguration.EndpointConfiguration.JwtSigningCredentials));
    }

    public static async Task RegisterCookieAsync(HttpContext context, EntityModel<UserEntityFieldsModel> user, CancellationToken cancellationToken)
    {
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(new ClaimsIdentity(
                BuildCookieClaims(user),
                CookieAuthenticationDefaults.AuthenticationScheme)),
            new AuthenticationProperties
            {
                IsPersistent = true, // persist cookie across browser sessions
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(TokenExpirationMinutes)
            });
    }
}
