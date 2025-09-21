// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using CrossCloudKit.Interfaces.Records;
using CrossCloudKit.Utilities.Common;
using Microsoft.AspNetCore.Http;
using ReflectiveForms.Core.Endpoints.Enums;
using ReflectiveForms.Core.Models.ReservedEntityTypes;

namespace ReflectiveForms.Core.Endpoints.Mapped.Api;

internal class Media : BaseEndpoint
{
    public override ImmutableHashSet<RequestHttpVerb> AllowedMethods()
    {
        return [RequestHttpVerb.Get];
    }

    protected override RequestBodyType ExpectedRequestBodyTypeOnPostPutPatch()
    {
        return RequestBodyType.NotRelevant;
    }

    private static readonly int[] AllowedPxValues = [150, 300, 512, 1024];

    protected override async Task<IResult> HandleAsync(HttpContext context, CancellationToken cancellationToken)
    {
        // Auth check
        var userFields = RequesterUser.NotNull().Fields;
        if (!userFields.CanUserDo("READ", RfReservedEntities.MediaEntityName))
        {
            return HttpStatusCode.Forbidden.ToResult("User does not have permission to perform this operation.");
        }

        // Validate query params
        var request = context.Request;
        if (!context.Request.TryGetEntityIdParameter(out var id, out var failedResult))
            return failedResult.NotNull();
        var px = -1;
        if (request.Query.TryGetValue("px", out var typeValues))
        {
            if (!int.TryParse(typeValues.ToString(), out px)
                || !AllowedPxValues.Contains(px))
            {
                return HttpStatusCode.BadRequest.ToResult("Url parameter -px- is optional, but when provided; must be one of the following: 150, 300, 512, 1024.");
            }
        }

        var fileName = $"{id}/{id}{(px == -1 ? "" : $"_{px}")}.png";

        var metadataResult = await RfConfiguration.RepositoryService.FileServiceConfiguration.FileService.GetFileMetadataAsync(
            RfConfiguration.RepositoryService.FileServiceConfiguration.MediaBucketName,
            fileName,
            cancellationToken);
        if (!metadataResult.IsSuccessful)
            return metadataResult.ErrorMessage.ToResult();

        var response = context.Response;
        var metadata = metadataResult.Data;

        AppendFileMetadata(response, metadata);

        var downloadResult = await RfConfiguration.RepositoryService.FileServiceConfiguration.FileService.DownloadFileAsync(
            RfConfiguration.RepositoryService.FileServiceConfiguration.MediaBucketName,
            fileName,
            new StringOrStream(response.Body, metadata.Size),
            null,
            cancellationToken);

        if (downloadResult.IsSuccessful) return Results.Empty;

        response.Clear();
        return downloadResult.ErrorMessage.ToResult();
    }

    private static void AppendFileMetadata(HttpResponse response, FileMetadata metadata)
    {
        // Standard headers
        response.Headers.ContentLength = metadata.Size;

        if (!string.IsNullOrWhiteSpace(metadata.ContentType))
            response.Headers.ContentType = metadata.ContentType;

        if (!string.IsNullOrWhiteSpace(metadata.Checksum))
            response.Headers.ETag = $"\"{metadata.Checksum}\""; // HTTP expects quotes

        if (metadata.LastModified.HasValue)
            response.Headers.LastModified = metadata.LastModified.Value.UtcDateTime.ToString("R", CultureInfo.InvariantCulture); // RFC1123

        if (metadata.CreatedAt.HasValue)
            response.Headers["Creation-Date"] = metadata.CreatedAt.Value.UtcDateTime.ToString("o", CultureInfo.InvariantCulture);

        // Additional metadata
        foreach (var kv in metadata.Properties)
            response.Headers[$"Property-{kv.Key}"] = kv.Value;

        // Tags
        foreach (var kv in metadata.Tags)
            response.Headers[$"Tag-{kv.Key}"] = kv.Value;
    }
}
