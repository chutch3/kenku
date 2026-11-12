using System.Net;
using Newtonsoft.Json.Linq;

namespace API.HttpRequesters;

internal static class BotChallenge
{
    internal static bool IsChallenge(int status, string? server, bool mitigated, string body)
    {
        if (mitigated)
            return true;
        if (status is not (403 or 503))
            return false;
        try
        {
            if (JObject.Parse(body).Value<string>("error") == "captcha_required")
                return true;
        }
        catch (Newtonsoft.Json.JsonException) { }
        return server?.Contains("cloudflare", StringComparison.OrdinalIgnoreCase) == true
               && (body.Contains("cf-chl-", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("/cdn-cgi/challenge-platform/", StringComparison.OrdinalIgnoreCase)
                   || body.Contains("Just a moment", StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<bool> IsChallengeAsync(HttpResponseMessage response, CancellationToken ct)
    {
        bool mitigated = response.Headers.TryGetValues("cf-mitigated", out IEnumerable<string>? values)
                         && values.Contains("challenge", StringComparer.OrdinalIgnoreCase);
        if (!mitigated && response.StatusCode is not (HttpStatusCode.Forbidden or HttpStatusCode.ServiceUnavailable))
            return false;
        return IsChallenge((int)response.StatusCode, response.Headers.Server.ToString(), mitigated,
            await response.Content.ReadAsStringAsync(ct));
    }
}

internal sealed class BotChallengeException(string origin) : HttpRequestException($"A browser challenge blocked {origin}.");
