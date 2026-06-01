namespace API.HttpRequesters;

public interface IHttpRequester
{
    internal Task<HttpResponseMessage> MakeRequest(string url, RequestType requestType, string? referrer = null,
        CancellationToken? cancellationToken = null);
}