// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.EndpointModels;
using ReflectiveForms.Core.Services;
using ReflectiveForms.Core.Utilities;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class Login : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Post];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.JsonObject;
    }

    public override bool IsAuthenticatedEndpoint() => false;

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var loginModel = RequestBodyJsonObject.NotNull().ToObjectWithPolymorphism<LoginInputModel>();
        if (loginModel == null
            || string.IsNullOrWhiteSpace(loginModel.EmailAddress)
            || string.IsNullOrWhiteSpace(loginModel.Password)
            || !NetworkUtilities.IsValidEmail(loginModel.EmailAddress))
            return HttpStatusCode.BadRequest.ToResult("Invalid login credentials.");

        // Validate CAPTCHA
        if (!loginModel.CaptchaAnswer.HasValue)
        {
            RfConfiguration.LogError("Login attempt without CAPTCHA answer.");
            return HttpStatusCode.BadRequest.ToResult("CAPTCHA answer is required. Please try again.");
        }

        if (!CaptchaService.ValidateCaptcha(context, loginModel.CaptchaAnswer.Value))
        {
            RfConfiguration.LogError("Invalid CAPTCHA answer for login attempt.");
            return HttpStatusCode.BadRequest.ToResult("Invalid CAPTCHA. Please try again.");
        }

        loginModel.EmailAddress = loginModel.EmailAddress.ToLowerInvariant();

        var foundUser = RfConfiguration.UserEntitiesCache.FindEntityByFilterAndGetCopy(u =>
            u.Fields.EmailAddress == loginModel.EmailAddress);
        if (foundUser == null)
            return HttpStatusCode.NotFound.ToResult("User not found.");

        var userFields = foundUser.Fields;
        if (userFields.PasswordSha256 != CryptographyUtilities.CalculateStringSha256(loginModel.Password))
            return HttpStatusCode.Unauthorized.ToResult("Invalid login credentials.");

        await JwtAndCookieManager.RegisterCookieAsync(context, foundUser, cancellationToken);

        return new LoginOutputModel
        {
            Token = JwtAndCookieManager.GenerateJwtToken(foundUser)
        }.ToResult();
    }
}
