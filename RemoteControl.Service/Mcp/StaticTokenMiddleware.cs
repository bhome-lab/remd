namespace RemoteControl.Service.Mcp;

public sealed class StaticTokenMiddleware(RequestDelegate next, ControlTokenProvider token)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await next(context);
            return;
        }

        if (!string.Equals(token.Token, context.Request.Headers["X-Admin-Token"].FirstOrDefault(), StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new TokenError(false, "invalid_token"));
            return;
        }
        await next(context);
    }
}

public sealed record TokenError(bool Ok, string Error);

