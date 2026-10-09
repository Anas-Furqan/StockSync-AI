using System.Buffers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using StockSyncAI.Api.Configuration;
using StockSyncAI.Api.Database;
using StockSyncAI.Api.Models;

namespace StockSyncAI.Api.Services;

public sealed partial class InvoiceService(
    ApplicationPaths paths,
    IInvoiceRepository repository,
    IOptions<StockSyncOptions> options,
    ILogger<InvoiceService> logger) : IInvoiceService
{
    private const int BufferSize = 81920;
    private readonly long _maximumBytes = options.Value.InvoiceMaxUploadBytes;

    private static readonly IReadOnlyDictionary<string, string> AllowedExtensions =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".pdf"] = "application/pdf",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".webp"] = "image/webp",
        };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(paths.InvoiceStorageDirectory);
        var recoveredDeletes = 0;
        var removedOrphans = 0;

        foreach (var invoice in await repository.GetDeletingAsync(cancellationToken))
        {
            var filePath = ResolveStoragePath(invoice.StorageKey);
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            await repository.DeleteMarkedAsync(invoice.Id, cancellationToken);
            recoveredDeletes++;
        }

        var referenced = await repository.GetManagedStorageKeysAsync(cancellationToken);
        foreach (var filePath in Directory.EnumerateFiles(paths.InvoiceStorageDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileName(filePath);
            if (TemporaryFilePattern().IsMatch(fileName) ||
                (ManagedFilePattern().IsMatch(fileName) && !referenced.Contains(fileName)))
            {
                File.Delete(filePath);
                removedOrphans++;
            }
        }

        logger.LogInformation(
            "Invoice storage is ready; recovered {RecoveredDeletes} deletes and removed {RemovedOrphans} incomplete files",
            recoveredDeletes,
            removedOrphans);
    }

    public async Task<InvoiceUploadResult> UploadAsync(
        Stream content,
        string originalFileName,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        if (!content.CanRead)
        {
            throw new InvoiceValidationException("The selected invoice file cannot be read.");
        }

        var existing = await repository.FindByIdempotencyKeyAsync(
            idempotencyKey,
            cancellationToken);
        if (existing is not null)
        {
            return new InvoiceUploadResult(existing, true);
        }

        var (safeOriginalName, extension) = ValidateOriginalFileName(originalFileName);
        Directory.CreateDirectory(paths.InvoiceStorageDirectory);
        var temporaryPath = Path.Combine(
            paths.InvoiceStorageDirectory,
            $".upload-{Guid.NewGuid():N}.tmp");
        var header = new byte[12];
        var headerLength = 0;
        long totalBytes = 0;
        string hash;

        try
        {
            using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
                try
                {
                    int bytesRead;
                    while ((bytesRead = await content.ReadAsync(
                               buffer.AsMemory(0, buffer.Length),
                               cancellationToken)) > 0)
                    {
                        totalBytes += bytesRead;
                        if (totalBytes > _maximumBytes)
                        {
                            throw new InvoiceValidationException(
                                $"Invoice files must not exceed {FormatMaximumSize()}.");
                        }

                        var headerBytes = Math.Min(header.Length - headerLength, bytesRead);
                        if (headerBytes > 0)
                        {
                            buffer.AsSpan(0, headerBytes).CopyTo(header.AsSpan(headerLength));
                            headerLength += headerBytes;
                        }

                        hasher.AppendData(buffer, 0, bytesRead);
                        await output.WriteAsync(
                            buffer.AsMemory(0, bytesRead),
                            cancellationToken);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            if (totalBytes == 0)
            {
                throw new InvoiceValidationException("The selected invoice file is empty.");
            }

            var detectedMimeType = DetectMimeType(header.AsSpan(0, headerLength));
            if (detectedMimeType is null ||
                !string.Equals(
                    AllowedExtensions[extension],
                    detectedMimeType,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvoiceValidationException(
                    "The file contents do not match a supported invoice format.");
            }

            hash = Convert.ToHexString(hasher.GetHashAndReset()).ToLowerInvariant();
            var id = Guid.NewGuid();
            var storageKey = $"{id:N}{extension}";
            var finalPath = ResolveStoragePath(storageKey);
            File.Move(temporaryPath, finalPath);

            var invoice = new InvoiceRecord(
                id,
                safeOriginalName,
                storageKey,
                extension,
                detectedMimeType,
                totalBytes,
                hash,
                DateTimeOffset.UtcNow,
                InvoiceRecord.UploadedStatus,
                idempotencyKey);

            try
            {
                await repository.InsertAsync(invoice, cancellationToken);
                logger.LogInformation(
                    "Stored invoice {InvoiceId} ({FileSize} bytes, {MimeType})",
                    invoice.Id,
                    invoice.FileSize,
                    invoice.MimeType);
                return new InvoiceUploadResult(invoice, false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                File.Delete(finalPath);
                existing = await repository.FindByIdempotencyKeyAsync(
                    idempotencyKey,
                    cancellationToken);
                if (existing is not null)
                {
                    return new InvoiceUploadResult(existing, true);
                }
                throw;
            }
            catch
            {
                File.Delete(finalPath);
                throw;
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public Task<InvoicePage> ListAsync(
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        repository.ListAsync(page, pageSize, cancellationToken);

    public async Task<InvoiceRecord> GetAsync(
        Guid id,
        CancellationToken cancellationToken = default) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new InvoiceNotFoundException();

    public async Task<InvoiceFile> OpenFileAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var invoice = await GetAsync(id, cancellationToken);
        var path = ResolveStoragePath(invoice.StorageKey);
        try
        {
            var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            return new InvoiceFile(invoice, stream);
        }
        catch (FileNotFoundException)
        {
            throw new InvoiceFileMissingException();
        }
        catch (DirectoryNotFoundException)
        {
            throw new InvoiceFileMissingException();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await repository.MarkDeletingAsync(id, cancellationToken)
            ?? throw new InvoiceNotFoundException();
        var path = ResolveStoragePath(invoice.StorageKey);
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            await repository.RestoreUploadedAsync(id, cancellationToken);
            throw new InvoiceStorageException(
                "The stored invoice file could not be deleted.",
                exception);
        }

        await repository.DeleteMarkedAsync(id, cancellationToken);
        logger.LogInformation("Deleted invoice {InvoiceId}", id);
    }

    private (string SafeName, string Extension) ValidateOriginalFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) ||
            fileName.Length > 255 ||
            !string.Equals(fileName, Path.GetFileName(fileName), StringComparison.Ordinal) ||
            fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            fileName.Any(char.IsControl))
        {
            throw new InvoiceValidationException("The invoice filename is invalid.");
        }

        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.ContainsKey(extension))
        {
            throw new InvoiceValidationException(
                "Supported invoice formats are PDF, JPG, JPEG, PNG, and WebP.");
        }

        return (fileName, extension);
    }

    private string ResolveStoragePath(string storageKey)
    {
        if (!ManagedFilePattern().IsMatch(storageKey) ||
            !string.Equals(storageKey, Path.GetFileName(storageKey), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The invoice storage key is invalid.");
        }

        var root = Path.GetFullPath(paths.InvoiceStorageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var resolved = Path.GetFullPath(Path.Combine(root, storageKey));
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The invoice storage key escaped managed storage.");
        }
        return resolved;
    }

    private static string? DetectMimeType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 5 && header[..5].SequenceEqual("%PDF-"u8))
        {
            return "application/pdf";
        }
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "image/jpeg";
        }
        if (header.Length >= 8 && header[..8].SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }
        if (header.Length >= 12 &&
            header[..4].SequenceEqual("RIFF"u8) &&
            header.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }
        return null;
    }

    private string FormatMaximumSize() => $"{_maximumBytes / (1024 * 1024)} MB";

    [GeneratedRegex(@"^[a-f0-9]{32}\.(pdf|jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex ManagedFilePattern();

    [GeneratedRegex(@"^\.upload-[a-f0-9]{32}\.tmp$", RegexOptions.IgnoreCase)]
    private static partial Regex TemporaryFilePattern();
}
