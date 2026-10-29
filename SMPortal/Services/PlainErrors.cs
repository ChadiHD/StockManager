using System.Net;
using System.Text.Json;

namespace SMPortal.Services;

// What an admin is told when a call fails. They run a store: "sign in again" or "try again" is
// something they can act on, and a status code, an exception's message or a validation dump is
// not. The technical detail goes to the browser console, for whoever they ask to look.
public static class PlainErrors
{
    /// <summary>
    /// <paramref name="whatFailed"/> as a sentence, such as "The feeds could not be synced.", with
    /// what to do next. The exception itself is logged, never shown.
    /// </summary>
    public static string Explain(Exception exception, string whatFailed)
    {
        Console.Error.WriteLine($"{whatFailed} {exception}");

        // No status means no answer: the request never reached the API, or the API never replied.
        return exception is HttpRequestException { StatusCode: null }
            ? $"{whatFailed} The server could not be reached. Check the connection and try again."
            : ForStatus((exception as HttpRequestException)?.StatusCode, whatFailed);
    }

    /// <summary>
    /// The API's own refusal when it sent one, otherwise <paramref name="whatFailed"/> with what
    /// to do next.
    /// </summary>
    /// <remarks>
    /// The API's refusals are sentences written for an admin — "Another store already has that
    /// key." — sent as plain text with a 4xx, and those are kept word for word. Anything else is
    /// for a developer: the framework's validation problem, an error page, or, in Development, a
    /// 500's stack trace, which is plain text too and is why a 5xx body is never shown.
    /// </remarks>
    public static async Task<string> Refusal(HttpResponseMessage response, string whatFailed)
    {
        var body = (await response.Content.ReadAsStringAsync()).Trim();
        var sentence = Sentence(response.Content.Headers.ContentType?.MediaType, body);

        var status = (int)response.StatusCode;
        if (status is >= 400 and < 500 && status != 401
            && !string.IsNullOrWhiteSpace(sentence) && sentence.Length <= 600 && !sentence.Contains('\n'))
        {
            return sentence;
        }

        Console.Error.WriteLine(
            $"{whatFailed} {status} from {response.RequestMessage?.Method} {response.RequestMessage?.RequestUri?.AbsolutePath}: {body}");

        return ForStatus(response.StatusCode, whatFailed);
    }

    // Plain text, a JSON string, or an object carrying a message, as the order and quote status
    // refusals send. Not application/problem+json: that is the framework's, not the API's words.
    private static string? Sentence(string? mediaType, string body)
    {
        if (mediaType == "text/plain") return body.Trim('"');
        if (mediaType != "application/json") return null;

        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.String => root.GetString(),
                JsonValueKind.Object when root.TryGetProperty("message", out var message)
                                          && message.ValueKind == JsonValueKind.String => message.GetString(),
                _ => null
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string ForStatus(HttpStatusCode? status, string whatFailed) => status switch
    {
        HttpStatusCode.BadRequest => $"{whatFailed} Check what was entered and try again.",
        HttpStatusCode.Unauthorized => $"{whatFailed} Your session has ended. Sign in again.",
        HttpStatusCode.Forbidden => $"{whatFailed} Your account is not allowed to do that.",
        HttpStatusCode.NotFound => $"{whatFailed} It may have been removed. Reload the page to see what is there now.",
        HttpStatusCode.Conflict => $"{whatFailed} Somebody may have changed it at the same time. Reload the page and try again.",
        HttpStatusCode.TooManyRequests => $"{whatFailed} There have been too many attempts. Wait a few minutes and try again.",
        _ => $"{whatFailed} Try again in a moment. If it keeps happening, ask whoever looks after this system."
    };
}
