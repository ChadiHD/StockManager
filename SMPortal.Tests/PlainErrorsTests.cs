using System.Net;
using System.Text;
using FluentAssertions;
using SMPortal.Services;
using Xunit;

namespace SMPortal.Tests;

// What an admin is shown when a call fails: the API's own sentence, or plain words, never a
// status code, an exception's message or a stack trace.
public class PlainErrorsTests
{
    private const string WhatFailed = "The store could not be created.";

    private static HttpResponseMessage Response(HttpStatusCode status, string body, string mediaType) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "Another store already has that key.", "text/plain")]
    [InlineData(HttpStatusCode.Conflict, "\"Another store already has that key.\"", "application/json")]
    [InlineData(HttpStatusCode.Conflict, "{\"message\":\"Another store already has that key.\"}", "application/json")]
    public async Task TheApisOwnRefusalIsKeptWordForWord(HttpStatusCode status, string body, string mediaType)
    {
        var shown = await PlainErrors.Refusal(Response(status, body, mediaType), WhatFailed);

        shown.Should().Be("Another store already has that key.");
    }

    [Fact]
    public async Task AServerErrorsDetailIsNeverShownEvenAsPlainText()
    {
        // In Development a 500 carries its stack trace as text/plain, which looks like a refusal.
        var body = "System.InvalidOperationException: Sequence contains no elements\n   at StockApi.Controllers.SiteController.Create()";

        var shown = await PlainErrors.Refusal(Response(HttpStatusCode.InternalServerError, body, "text/plain"), WhatFailed);

        shown.Should().StartWith(WhatFailed).And.Contain("Try again in a moment");
        shown.Should().NotContain("Exception").And.NotContain("500");
    }

    [Fact]
    public async Task TheFrameworksValidationProblemIsReplacedWithWhatToDo()
    {
        var body = "{\"type\":\"https://tools.ietf.org/html/rfc9110#section-15.5.1\",\"title\":\"One or more validation errors occurred.\",\"status\":400}";

        var shown = await PlainErrors.Refusal(Response(HttpStatusCode.BadRequest, body, "application/problem+json"), WhatFailed);

        shown.Should().Be($"{WhatFailed} Check what was entered and try again.");
    }

    [Fact]
    public void AnUnansweredRequestSaysTheServerCouldNotBeReached()
    {
        var shown = PlainErrors.Explain(new HttpRequestException("No such host is known. (localhost:7042)"), WhatFailed);

        shown.Should().Be($"{WhatFailed} The server could not be reached. Check the connection and try again.");
    }

    [Fact]
    public void AnEndedSessionSaysToSignInAgain()
    {
        var shown = PlainErrors.Explain(
            new HttpRequestException("Response status code does not indicate success: 401 (Unauthorized).", null, HttpStatusCode.Unauthorized),
            WhatFailed);

        shown.Should().Be($"{WhatFailed} Your session has ended. Sign in again.");
    }
}
