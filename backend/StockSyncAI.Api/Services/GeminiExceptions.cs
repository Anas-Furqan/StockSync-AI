namespace StockSyncAI.Api.Services;

public class GeminiException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class GeminiNotConfiguredException()
    : GeminiException("Gemini is not configured. Add GEMINI_API_KEY and restart StockSync AI.");

public sealed class GeminiAuthenticationException()
    : GeminiException("Gemini rejected the configured API key.");

public sealed class GeminiRateLimitException()
    : GeminiException("Gemini is temporarily rate limited. Try again later.");

public sealed class GeminiUnavailableException(string message, Exception? innerException = null)
    : GeminiException(message, innerException);

public sealed class GeminiResponseException(string message, Exception? innerException = null)
    : GeminiException(message, innerException);
