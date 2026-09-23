namespace VideoAutomation.Shared.Exceptions;

public class ApiException : Exception
{
    public int StatusCode { get; }
    public string? ServiceName { get; }

    public ApiException(string message, int statusCode = 500, string? serviceName = null)
        : base(message)
    {
        StatusCode = statusCode;
        ServiceName = serviceName;
    }

    public ApiException(string message, Exception innerException, int statusCode = 500, string? serviceName = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
        ServiceName = serviceName;
    }
}

public class ScrapingException : ApiException
{
    public string Platform { get; }

    public ScrapingException(string platform, string message, Exception? innerException = null)
        : base($"[{platform}] Scraping error: {message}", innerException ?? new Exception(), 503, platform)
    {
        Platform = platform;
    }
}

public class VideoGenerationException : ApiException
{
    public VideoGenerationException(string message, Exception? innerException = null)
        : base($"Video generation error: {message}", innerException ?? new Exception(), 500, "VideoGenerator")
    { }
}

public class AnalysisException : ApiException
{
    public AnalysisException(string message, Exception? innerException = null)
        : base($"Analysis error: {message}", innerException ?? new Exception(), 502, "Analyzer")
    { }
}

public class PublishingException : ApiException
{
    public string Platform { get; }

    public PublishingException(string platform, string message, Exception? innerException = null)
        : base($"[{platform}] Publishing error: {message}", innerException ?? new Exception(), 503, platform)
    {
        Platform = platform;
    }
}
