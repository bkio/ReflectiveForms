// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Net;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ReflectiveForms.Core.Endpoints.Enums;

internal static class ResponseExtensions
{
    internal static IResult ToResult(this string? message, HttpStatusCode statusCode)
    {
        if (message != null)
        {
            return statusCode.ToResult(message);
        }
        return new JObject
        {
            ["detail"] = message
        }.ToResult(500);
    }

    internal static IResult ToResult(this HttpStatusCode status, string? message)
    {
        return new JObject
        {
            ["detail"] = message
        }.ToResult((int)status);
    }

    internal static IResult ToResult(this JToken? jToken, int statusCode = 200) =>
        Results.Content(
            jToken != null
                ? jToken.ToString(Formatting.None)
                : "{}",
            "application/json", System.Text.Encoding.UTF8, statusCode);

    internal static IResult ToResult(this object? obj, int statusCode = 200) =>
        Results.Content(
            obj != null
                ? JsonConvert.SerializeObject(obj)
                : "{}"
            , "application/json", System.Text.Encoding.UTF8, statusCode);
}
