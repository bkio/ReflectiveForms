// Copyright (c) 2022- Burak Kara, AGPL-3.0 license
// See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Net;
using CrossCloudKit.Interfaces.Classes;
using CrossCloudKit.Utilities.Common;
using SkiaSharp;

namespace ReflectiveForms.Core.Models.ReservedEntityTypes;

internal static class MediaEntityHookOnChanged
{
    private record BitmapRecord(SKBitmap Bitmap, bool IsOriginal = false);
    private static async Task OnMediaUpsert(int id, EntityModel<MediaEntityFieldsModel> newEntity, CancellationToken cancellationToken)
    {
        var mediaFields = newEntity.Fields;

        if (string.IsNullOrEmpty(mediaFields.MediaSource)
            || mediaFields.MediaSource.StartsWith($"/{RfEndpointMapper.MediaEndpoint}"))
        {
            //No need to process. Already processed.
            return;
        }

        if (mediaFields.MediaSource.Contains(','))
        {
            mediaFields.MediaSource = mediaFields.MediaSource[(mediaFields.MediaSource.IndexOf(',') + 1)..];
        }

        try
        {
            var imageBytes = Convert.FromBase64String(mediaFields.MediaSource);
            await using var originalImageStream = new MemoryTributary(imageBytes);

            using var bmOriginal = SKBitmap.Decode(originalImageStream);
            if (bmOriginal == null)
            {
                RfConfiguration.LogError("OnMediaUpsert: Decode failed - Original file");
                return;
            }

            using var bm150 = CloneAndResize(bmOriginal, 150);
            using var bm300 = CloneAndResize(bmOriginal, 300);
            using var bm512 = CloneAndResize(bmOriginal, 512);
            using var bm1024 = CloneAndResize(bmOriginal, 1024);

            var errors = new ConcurrentBag<string>();
            var tasks = new List<BitmapRecord>
            {
                new(bm150), new(bm300), new(bm512), new(bm1024), new(bmOriginal, true)
            }.Select(async bmPair =>
            {
                var (bitmap, isOriginal) = bmPair;
                var res = await UploadImageAsync(id, bitmap, isOriginal, cancellationToken);
                if (!res.IsSuccessful)
                {
                    errors.Add(res.ErrorMessage);

                    var deleteResult = await TryDeletingMediaItemAsync(id, isOriginal, bitmap.Width, cancellationToken);
                    if (!deleteResult.IsSuccessful)
                    {
                        errors.Add(deleteResult.ErrorMessage);
                    }
                }
            });

            await Task.WhenAll(tasks);

            if (!errors.IsEmpty)
                RfConfiguration.LogError("OnMediaUpsert: Errors occured during OnMediaUpsert: " + string.Join(Environment.NewLine, errors));
        }
        catch (Exception e)
        {
            RfConfiguration.LogError($"OnMediaUpsert: Failed to process media with exception: {e}");

            var deleteResult = await TryForceDeleteAllMediaItemsAsync(id, cancellationToken);
            if (!deleteResult.IsSuccessful)
                RfConfiguration.LogError($"OnMediaUpsert: TryForceDeleteAllMediaItemsAsync has failed with (upon exception-revert): {deleteResult.ErrorMessage}");
        }
    }

    internal static async Task OnMediaDeleted(PostDeleteHookModel<MediaEntityFieldsModel> hookModel, CancellationToken cancellationToken)
    {
        var deleteResult = await TryForceDeleteAllMediaItemsAsync(hookModel.Id, cancellationToken);
        if (!deleteResult.IsSuccessful)
            RfConfiguration.LogError($"OnMediaDeleted: {deleteResult.ErrorMessage}");
    }

    private static SKBitmap CloneAndResize(SKBitmap originalImage, int newWidth)
    {
        var ratio = originalImage.Width / (float)originalImage.Height;
        var newHeight = (int)(newWidth / ratio);
        var resizedImage = new SKBitmap(newWidth, newHeight);
        originalImage.ScalePixels(resizedImage, new SKSamplingOptions(SKCubicResampler.Mitchell));
        return resizedImage;
    }

    private static async Task<OperationResult<bool>> UploadImageAsync(int mediaId, SKBitmap image, bool isOriginalImage, CancellationToken cancellationToken)
    {
        try
        {
            await using var imageStream = new MemoryTributary();
            using var img = SKImage.FromBitmap(image);
            using var data = img.Encode(SKEncodedImageFormat.Png, 100);

            data.SaveTo(imageStream);

            imageStream.Position = 0;

            var uploadResult = await RfConfiguration.RepositoryService.FileServiceConfiguration.FileService.UploadFileAsync(
                new StringOrStream(imageStream, imageStream.Length),
                RfConfiguration.RepositoryService.FileServiceConfiguration.MediaBucketName,
                $"{mediaId}/{mediaId}{(isOriginalImage ? "" : $"_{image.Width}")}.png",
                cancellationToken: cancellationToken);

            if (!uploadResult.IsSuccessful)
            {
                return OperationResult<bool>.Failure($"Upload failed for media id {mediaId}, original image?: {isOriginalImage}, px: {image.Width}", uploadResult.StatusCode);
            }
        }
        catch (Exception e)
        {
            return OperationResult<bool>.Failure($"Upload failed for media id {mediaId}, original image?: {isOriginalImage}, px: {image.Width}. Exception: {e.Message}", HttpStatusCode.InternalServerError);
        }
        return OperationResult<bool>.Success(true);
    }

    private static async Task<OperationResult<bool>> TryForceDeleteAllMediaItemsAsync(int mediaId, CancellationToken cancellationToken)
    {
        var errors = new ConcurrentBag<string>();
        var tasks = new List<int> { -1, 150, 300, 512, 1024 }.Select(async px =>
        {
            var deleteResult = await TryDeletingMediaItemAsync(mediaId, px == -1, -1, cancellationToken);
            if (!deleteResult.IsSuccessful)
                errors.Add(deleteResult.ErrorMessage);
        });
        await Task.WhenAll(tasks);

        return !errors.IsEmpty
            ? OperationResult<bool>.Failure("Errors occured during TryForceDeleteAllMediaItemsAsync: " + string.Join(Environment.NewLine, errors), HttpStatusCode.InternalServerError)
            : OperationResult<bool>.Success(true);
    }

    private static async Task<OperationResult<bool>> TryDeletingMediaItemAsync(int mediaId, bool bIsOriginalImage, int ifResizedWidthPx, CancellationToken cancellationToken)
    {
        var deleteResult = await RfConfiguration.RepositoryService.FileServiceConfiguration.FileService.DeleteFileAsync(
            RfConfiguration.RepositoryService.FileServiceConfiguration.MediaBucketName,
            $"{mediaId}/{mediaId}{(bIsOriginalImage ? "" : $"_{ifResizedWidthPx}")}.png",
            cancellationToken: cancellationToken);
        return !deleteResult.IsSuccessful
            ? OperationResult<bool>.Failure($"Delete failed for media id {mediaId}, original image?: {bIsOriginalImage}, px: {ifResizedWidthPx}", deleteResult.StatusCode)
            : OperationResult<bool>.Success(true);
    }

    internal static async Task OnMediaUpdated(PostUpdateHookModel<MediaEntityFieldsModel> hookModel, CancellationToken cancellationToken) => await OnMediaUpsert(hookModel.Id, hookModel.NewFinalBody, cancellationToken);
    internal static async Task OnMediaCreated(PostCreateHookModel<MediaEntityFieldsModel> hookModel, CancellationToken cancellationToken) => await OnMediaUpsert(hookModel.NewId, hookModel.FinalBody, cancellationToken);
}
