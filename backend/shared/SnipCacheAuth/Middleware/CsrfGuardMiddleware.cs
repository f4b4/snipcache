using Microsoft.AspNetCore.Http;

namespace SnipCacheAuth;

/// <summary>
/// Rejects mutating requests to /api that lack the X-CSRF header.
/// Cookie auth + SameSite=Lax stops cross-origin GETs from carrying the cookie,
/// but POSTs from plain HTML forms still would. Requiring a custom header that
/// only fetch/XHR can set blocks those form-based attacks — browsers won't add
/// a custom header on a plain &lt;form&gt; submit, and CORS blocks cross-origin
/// scripts from setting it.
/// </summary>
public sealed class CsrfGuardMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Short-circuits a mutating (POST/PUT/PATCH/DELETE) <c>/api</c> request with 403 when it
    /// lacks the <c>X-CSRF</c> header; otherwise passes the request to the next middleware.
    /// </summary>
    public async Task InvokeAsync(HttpContext ctx)
    {
        var method = ctx.Request.Method;
        var isMutating = HttpMethods.IsPost(method)
            || HttpMethods.IsPut(method)
            || HttpMethods.IsPatch(method)
            || HttpMethods.IsDelete(method);

        if (isMutating
            && ctx.Request.Path.StartsWithSegments("/api")
            && !ctx.Request.Headers.ContainsKey("X-CSRF"))
        {
            ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        await next(ctx);
    }
}
