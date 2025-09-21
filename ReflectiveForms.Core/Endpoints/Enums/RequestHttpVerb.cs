// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

namespace ReflectiveForms.Core.Endpoints.Enums;

public enum RequestHttpVerb
{
    Get,
    Post,
    Put,
    Delete,
    Patch,
    Head,
    Options
}
public static class RequestHttpVerbExtensions
{
    public static string ToHttpMethodString(this RequestHttpVerb verb)
        => verb.ToString().ToUpperInvariant();

    public static bool IsHttpMethod(this string verbCandidate, out RequestHttpVerb verbEnum)
    {
        if (!string.IsNullOrWhiteSpace(verbCandidate))
            return Enum.TryParse(
                verbCandidate,
                ignoreCase: true,
                out verbEnum
            );

        verbEnum = default;
        return false;
    }
}
