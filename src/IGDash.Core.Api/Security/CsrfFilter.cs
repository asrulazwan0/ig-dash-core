using Microsoft.AspNetCore.Antiforgery;

namespace IGDash.Core.Api.Security;

internal sealed class CsrfFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        try { await context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context.HttpContext); }
        catch (AntiforgeryValidationException) { return Results.Problem(statusCode: 403, title: "Request verification failed. Refresh and try again."); }
        return await next(context);
    }
}
