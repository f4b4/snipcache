using SnipCacheAuth;
using Microsoft.AspNetCore.Http;

namespace SnipCacheAuth.Tests;

// The CSRF guard blocks mutating /api requests that lack the X-CSRF header — the
// form-POST defense for the cookie session. These tests pin the four axes that
// decide whether it short-circuits: HTTP verb, path, and header presence.
public class CsrfGuardMiddlewareTests
{
    private static async Task<(int StatusCode, bool NextCalled)> Invoke(
        string method, string path, bool withCsrfHeader)
    {
        var nextCalled = false;
        var middleware = new CsrfGuardMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        var ctx = new DefaultHttpContext();
        ctx.Request.Method = method;
        ctx.Request.Path = path;
        if (withCsrfHeader) ctx.Request.Headers["X-CSRF"] = "1";

        await middleware.InvokeAsync(ctx);
        return (ctx.Response.StatusCode, nextCalled);
    }

    [Fact]
    public async Task GetToApi_WithoutHeader_PassesThrough()
    {
        var (status, nextCalled) = await Invoke("GET", "/api/things", withCsrfHeader: false);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task MutatingApi_WithoutHeader_Returns403AndShortCircuits(string method)
    {
        var (status, nextCalled) = await Invoke(method, "/api/things", withCsrfHeader: false);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status403Forbidden, status);
    }

    [Fact]
    public async Task MutatingApi_WithHeader_PassesThrough()
    {
        var (status, nextCalled) = await Invoke("POST", "/api/things", withCsrfHeader: true);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task MutatingNonApi_WithoutHeader_PassesThrough()
    {
        // The guard defends /api only; a POST elsewhere (e.g. /auth/logout) is unaffected.
        var (status, nextCalled) = await Invoke("POST", "/auth/logout", withCsrfHeader: false);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }

    [Fact]
    public async Task MutatingApiLookalike_WithoutHeader_PassesThrough()
    {
        // StartsWithSegments is segment-aware: "/apidocs" is not under "/api".
        var (status, nextCalled) = await Invoke("POST", "/apidocs", withCsrfHeader: false);

        Assert.True(nextCalled);
        Assert.Equal(StatusCodes.Status200OK, status);
    }
}
