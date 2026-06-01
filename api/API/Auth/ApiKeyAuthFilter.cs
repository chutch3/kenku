using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace API.Auth;

/// <summary>
/// Validates the Mylar-style <c>apikey</c> query parameter against the configured
/// <see cref="KenkuSettings.ApiKey"/>. Resolves settings from the request services so
/// the attribute can stay parameterless.
/// </summary>
public class ApiKeyAuthFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var settings = context.HttpContext.RequestServices.GetService(typeof(KenkuSettings)) as KenkuSettings;
        var configuredKey = settings?.ApiKey;

        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var providedKey = context.HttpContext.Request.Query["apikey"].ToString();
        if (string.IsNullOrWhiteSpace(providedKey) || !FixedTimeEquals(configuredKey, providedKey))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        await next();
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        var ba = Encoding.UTF8.GetBytes(a);
        var bb = Encoding.UTF8.GetBytes(b);
        if (ba.Length != bb.Length)
            return false;
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}

/// <summary>
/// Applies <see cref="ApiKeyAuthFilter"/> to a controller or action.
/// </summary>
public class RequireApiKeyAttribute : TypeFilterAttribute
{
    public RequireApiKeyAttribute() : base(typeof(ApiKeyAuthFilter))
    {
    }
}
