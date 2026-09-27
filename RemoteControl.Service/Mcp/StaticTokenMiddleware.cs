namespace RemoteControl.Service.Mcp;

public sealed class StaticTokenMiddleware(RequestDelegate next, IConfiguration configuration)
{
    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.StartsWithSegments("/healthz"))
        {
            await next(context);
            return;
        }

        var expected = configuration["Control:Token"]
            ?? Environment.GetEnvironmentVariable("REMOTE_CONTROL_TOKEN")
            ?? "change-me";
        if (!string.Equals(expected, context.Request.Headers["X-Admin-Token"].FirstOrDefault(), StringComparison.Ordinal))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new TokenError(false, "invalid_token"));
            return;
        }
        await next(context);
    }
}

public sealed record TokenError(bool Ok, string Error);

