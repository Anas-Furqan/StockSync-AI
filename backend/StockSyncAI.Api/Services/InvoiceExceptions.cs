namespace StockSyncAI.Api.Services;

public sealed class InvoiceValidationException(string message) : Exception(message);

public sealed class InvoiceNotFoundException()
    : Exception("The requested invoice was not found.");

public sealed class InvoiceFileMissingException()
    : Exception("The stored invoice file is missing.");

public sealed class InvoiceStorageException(string message, Exception innerException)
    : Exception(message, innerException);
