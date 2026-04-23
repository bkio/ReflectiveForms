// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

// ReSharper disable MemberCanBePrivate.Global

namespace ReflectiveForms.Core.Endpoints;

public abstract class BaseEndpoint
{
    public abstract ImmutableHashSet<RequestHttpVerb> AllowedMethods();
    protected abstract RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch();
    public virtual bool IsAuthenticatedEndpoint() => true;

    protected abstract Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken);

    protected byte[]? RequestBodyRawBytes { get; private set; }

    protected string? RequestBodyRawString { get; private set; }

    protected JObject? RequestBodyJsonObject { get; private set; }

    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    protected JArray? RequestBodyJsonArray { get; private set; }
    protected EntityModel<UserEntityFieldsModel>? RequesterUser { get; private set; }
    protected bool IsRequesterRootUser { get; private set; }

    public async Task<IResult> InvokeAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var expectedBodyType = ExpectedRequestBodyTypeOnPostPutPatch();

        var bodyHandled = await HandleBodyAsync(context, expectedBodyType, cancellationToken);
        if (bodyHandled != null)
        {
            return bodyHandled;
        }

        if (context.User.Identity is not { IsAuthenticated: true })
            return await HandleAsync(context, cancellationToken);

        var userIdStr = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
                        ?? context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (userIdStr == null || !int.TryParse(userIdStr, out var userId))
            return HttpStatusCode.InternalServerError.ToResult("JWT/Cookie is corrupted. User ID is missing.");

        var user = RfConfiguration.UserEntitiesCache.GetEntityCopy(userId);
        if (user == null)
            return HttpStatusCode.Unauthorized.ToResult("Incorrect credentials.");

        RequesterUser = user;
        IsRequesterRootUser = user.Title.Text == RootManager.RootUserTitle;

        return await HandleAsync(context, cancellationToken);
    }

    private async Task<IResult?> HandleBodyAsync(HttpContext context, RequestBodyType expectedBodyType, CancellationToken cancellationToken)
    {
        if (expectedBodyType == RequestBodyType.NotRelevant
            || !context.Request.Method.IsHttpMethod(out var verb)
            || verb is not (RequestHttpVerb.Post or RequestHttpVerb.Put or RequestHttpVerb.Patch))
            return null;

        try
        {
            await using var mStream = new MemoryTributary();
            await context.Request.Body.CopyToAsync(mStream, cancellationToken);
            RequestBodyRawBytes = mStream.ToArray();
        }
        catch (OperationCanceledException)
        {
            // Client disconnected — let ASP.NET handle gracefully
            throw;
        }
        catch (Exception e)
        {
            RfConfiguration.LogError("Request failed during body-read. Error: {Exception}", e);
            return HttpStatusCode.BadRequest.ToResult("Request failed during body-read.");
        }

        if (expectedBodyType == RequestBodyType.RawBytes) return null;

        try
        {
            RequestBodyRawString = Encoding.UTF8.GetString(RequestBodyRawBytes);
        }
        catch (Exception e)
        {
            RfConfiguration.LogError("Request failed during body-read (bytes to string). Error: {Exception}", e);
            return HttpStatusCode.BadRequest.ToResult("Request failed during body-read.");
        }

        if (expectedBodyType == RequestBodyType.RawString) return null;

        try
        {
            var requestJsonBody = JToken.Parse(RequestBodyRawString);

            if (expectedBodyType == RequestBodyType.JsonObject)
            {
                if (requestJsonBody.Type != JTokenType.Object)
                    return HttpStatusCode.BadRequest.ToResult("Request body is not a JSON object.");
                RequestBodyJsonObject = (JObject)requestJsonBody;
            }
            if (expectedBodyType == RequestBodyType.JsonArray)
            {
                if (requestJsonBody.Type != JTokenType.Array)
                    return HttpStatusCode.BadRequest.ToResult("Request body is not a JSON array.");
                RequestBodyJsonArray = (JArray)requestJsonBody;
            }
        }
        catch (Exception e)
        {
            RfConfiguration.LogError("Request failed during body-read (string to json). Error: {Exception}", e);
            return HttpStatusCode.BadRequest.ToResult("Request failed during body-read.");
        }
        return null;
    }
}
