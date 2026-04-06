// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Net;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class BulkRead : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Post];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.JsonObject;
    }

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var body = RequestBodyJsonObject.NotNull();

        if (!body.TryGetValue("sources", out var sourcesToken) || sourcesToken is not JArray sourcesArray)
            return HttpStatusCode.BadRequest.ToResult("Request body must contain a 'sources' array.");

        var userFields = RequesterUser.NotNull().Fields;
        var results = new JArray();
        var unauthorized = new JArray();

        foreach (var sourceToken in sourcesArray)
        {
            if (sourceToken is not JObject sourceObj)
                continue;

            var entityName = sourceObj.Value<string>("entity");
            if (string.IsNullOrWhiteSpace(entityName))
                continue;

            if (!RfConfiguration.EntityNameToConfiguration.ContainsKey(entityName))
                continue;

            if (!userFields.CanUserDo("PEEK_ALL", entityName))
            {
                unauthorized.Add(entityName);
                continue;
            }

            var readResult = await RfConfiguration.RepositoryService.FullReadAllAsync(entityName, cancellationToken);
            if (!readResult.IsSuccessful)
                continue;

            var rows = readResult.Data;

            // Field-level filtering: if 'fields' array is provided, strip unwanted properties
            HashSet<string>? requestedFields = null;
            if (sourceObj.TryGetValue("fields", out var fieldsToken) && fieldsToken is JArray fieldsArray)
            {
                requestedFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "id" };
                foreach (var ft in fieldsArray)
                {
                    var fieldName = ft.Value<string>();
                    if (!string.IsNullOrWhiteSpace(fieldName))
                        requestedFields.Add(fieldName);
                }
            }

            JArray filteredRows;
            if (requestedFields != null)
            {
                filteredRows = new JArray();
                foreach (var rowToken in rows)
                {
                    if (rowToken is not JObject rowObj) continue;

                    // Only keep "id" and the filtered "fields" object
                    var filteredRow = new JObject();
                    if (rowObj.TryGetValue("id", out var idVal))
                        filteredRow["id"] = idVal.DeepClone();

                    if (rowObj.TryGetValue("fields", out var fieldsVal) && fieldsVal is JObject fieldsObj)
                    {
                        var filteredFields = new JObject();
                        foreach (var fp in fieldsObj.Properties())
                        {
                            if (requestedFields.Contains(fp.Name))
                                filteredFields.Add(fp.Name, fp.Value.DeepClone());
                        }
                        filteredRow["fields"] = filteredFields;
                    }

                    filteredRows.Add(filteredRow);
                }
            }
            else
            {
                filteredRows = rows;
            }

            results.Add(new JObject
            {
                ["entity"] = entityName,
                ["total_count"] = filteredRows.Count,
                ["rows"] = filteredRows
            });
        }

        var response = new JObject
        {
            ["results"] = results,
            ["unauthorized"] = unauthorized
        };

        return response.ToResult();
    }
}
