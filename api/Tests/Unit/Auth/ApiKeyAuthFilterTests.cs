using API;
using API.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.Unit.Auth;

public class ApiKeyAuthFilterTests
{
    [Fact]
    public async Task MissingApiKey_ReturnsUnauthorized()
    {
        var ctx = BuildContext(settingsKey: "secret", queryKey: null);

        await new ApiKeyAuthFilter().OnActionExecutionAsync(ctx.Executing, ctx.Next);

        Assert.IsType<UnauthorizedResult>(ctx.Executing.Result);
        Assert.False(ctx.NextCalled);
    }

    [Fact]
    public async Task BlankApiKey_ReturnsUnauthorized()
    {
        var ctx = BuildContext(settingsKey: "secret", queryKey: "   ");

        await new ApiKeyAuthFilter().OnActionExecutionAsync(ctx.Executing, ctx.Next);

        Assert.IsType<UnauthorizedResult>(ctx.Executing.Result);
        Assert.False(ctx.NextCalled);
    }

    [Fact]
    public async Task WrongApiKey_ReturnsUnauthorized()
    {
        var ctx = BuildContext(settingsKey: "secret", queryKey: "nope");

        await new ApiKeyAuthFilter().OnActionExecutionAsync(ctx.Executing, ctx.Next);

        Assert.IsType<UnauthorizedResult>(ctx.Executing.Result);
        Assert.False(ctx.NextCalled);
    }

    [Fact]
    public async Task CorrectApiKey_CallsNext()
    {
        var ctx = BuildContext(settingsKey: "secret", queryKey: "secret");

        await new ApiKeyAuthFilter().OnActionExecutionAsync(ctx.Executing, ctx.Next);

        Assert.Null(ctx.Executing.Result);
        Assert.True(ctx.NextCalled);
    }

    [Fact]
    public async Task EmptySettingsKey_AlwaysUnauthorized()
    {
        var ctx = BuildContext(settingsKey: "", queryKey: "");

        await new ApiKeyAuthFilter().OnActionExecutionAsync(ctx.Executing, ctx.Next);

        Assert.IsType<UnauthorizedResult>(ctx.Executing.Result);
        Assert.False(ctx.NextCalled);
    }

    private sealed class Ctx
    {
        public ActionExecutingContext Executing = null!;
        public ActionExecutionDelegate Next = null!;
        public bool NextCalled;
    }

    private static Ctx BuildContext(string settingsKey, string? queryKey)
    {
        var settings = new KenkuSettings { ApiKey = settingsKey };
        var services = new ServiceCollection();
        services.AddSingleton(settings);
        var httpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        if (queryKey != null)
            httpContext.Request.QueryString = QueryString.Create("apikey", queryKey);

        var actionContext = new ActionContext(httpContext, new RouteData(), new ActionDescriptor());
        var executing = new ActionExecutingContext(actionContext, new List<IFilterMetadata>(), new Dictionary<string, object?>(), controller: null!);

        var ctx = new Ctx { Executing = executing };
        ctx.Next = () =>
        {
            ctx.NextCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, new List<IFilterMetadata>(), controller: null!));
        };
        return ctx;
    }
}
