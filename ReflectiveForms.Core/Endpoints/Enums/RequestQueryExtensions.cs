// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Microsoft.AspNetCore.Http;

namespace ReflectiveForms.Core.Endpoints.Enums;

internal static class RequestQueryExtensions
{
    internal static bool TryGetTypeParameter(
        this HttpRequest request,
        out string parameterValue,
        out IResult? failedResult,
        bool onFailureJsonResponse = true)
    {
        if (!request.Query.TryGetValue("type", out var typeValues))
        {
            parameterValue = "";
            failedResult = onFailureJsonResponse
                ? HttpStatusCode.BadRequest.ToResult("Url parameter -type- is mandatory and must match an expected entity type.")
                : Results.BadRequest("Url parameter -type- is mandatory and must match an expected entity type.");
            return false;
        }
        parameterValue = typeValues.ToString().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(parameterValue))
        {
            failedResult = onFailureJsonResponse
                ? HttpStatusCode.BadRequest.ToResult("Url parameter -type- is mandatory and must match an expected entity type.")
                : Results.BadRequest("Url parameter -type- is mandatory and must match an expected entity type.");
            return false;
        }
        failedResult = null;
        return true;
    }


    internal static bool TryCrudOperationTypeParameter(
        this HttpRequest request,
        out string parameterValue,
        out IResult? failedResult,
        bool onFailureJsonResponse = true)
    {
        if (!request.Query.TryGetValue("operation", out var opValues))
        {
            parameterValue = "";
            failedResult = onFailureJsonResponse
                ? HttpStatusCode.BadRequest.ToResult("Url parameter -operation- is mandatory.")
                : Results.BadRequest("Url parameter -operation- is mandatory.");
            return false;
        }
        parameterValue = opValues.ToString().ToUpperInvariant();
        if (parameterValue is not ("CREATE" or "READ" or "PEEK_ALL" or "PEEK_ALL_PAGINATED" or "UPDATE" or "DELETE" or "HISTORY"))
        {
            failedResult = onFailureJsonResponse
                ? HttpStatusCode.BadRequest.ToResult("Url parameter -operation- must be one of: CREATE,READ,PEEK_ALL,PEEK_ALL_PAGINATED,UPDATE,DELETE,HISTORY")
                : Results.BadRequest("Url parameter -operation- must be one of: CREATE,READ,PEEK_ALL,PEEK_ALL_PAGINATED,UPDATE,DELETE,HISTORY");
            return false;
        }
        failedResult = null;
        return true;
    }

    internal static bool TryGetEntityIdParameter(
        this HttpRequest request,
        out int parameterValue,
        out IResult? failedResult,
        bool onFailureJsonResponse = true)
    {
        if (!request.Query.TryGetValue("id", out var idValues)
            || !int.TryParse(idValues.ToString(), out parameterValue))
        {
            parameterValue = -1;
            failedResult = onFailureJsonResponse
                ? HttpStatusCode.BadRequest.ToResult("Url parameter -id- is mandatory and must be an integer.")
                : Results.BadRequest("Url parameter -id- is mandatory and must be an integer.");
            return false;
        }
        failedResult = null;
        return true;
    }
}
