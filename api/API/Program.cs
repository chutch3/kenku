using API.HttpRequesters.Interfaces;
using System.Reflection;
using API;
using API.HttpRequesters;
using API.Extensions;
using Asp.Versioning;
using Asp.Versioning.Builder;
using Asp.Versioning.Conventions;
using log4net;
using log4net.Config;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using Newtonsoft.Json.Converters;

string kenkuBanner =
    "\n\n" +
    " _______                                 v2\n" +
    "|_     _|.----..---.-..-----..-----..---.-.\n" +
    "  |   |  |   _||  _  ||     ||  _  ||  _  |\n" +
    "  |___|  |__|  |___._||__|__||___  ||___._|\n" +
    "                             |_____|       \n" +
    $"Built at {BuildInformation.BuildAt} for {BuildInformation.Platform} version {BuildInformation.DotNetSdkVersion}\n" +
    $"branch: {ThisAssembly.Git.Branch} commit: {ThisAssembly.Git.Commit} tag: {ThisAssembly.Git.Tag}\n\n";

XmlConfigurator.ConfigureAndWatch(new FileInfo("Log4Net.config.xml"));
ILog log = LogManager.GetLogger("Startup");
log.Info(kenkuBanner);
log.Info("Logger Configured.");

log.Info("Starting up");
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

log.Debug("Loading Settings...");
// Production loads settings from disk. Integration tests pass Kenku:AppData to use an isolated,
// writable settings location without reading/writing the real file or the process-wide APP_DATA env.
var settings = builder.Configuration["Kenku:AppData"] is { } appDataOverride
    ? new KenkuSettings { AppData = appDataOverride }
    : KenkuSettings.Load();
builder.Services.AddSingleton(settings);

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll",
        policy =>
        {
            // Restrict to configured origins when any are set; otherwise fall back to allow-any.
            if (settings.CorsAllowAnyOrigin)
                policy.AllowAnyOrigin();
            else
                policy.WithOrigins(settings.CorsAllowedOrigins);
            policy
                .AllowAnyMethod()
                .AllowAnyHeader();
        });
});

log.Debug("Adding API-Explorer-helpers...");
builder.Services.AddApiVersioning(option =>
    {
        option.AssumeDefaultVersionWhenUnspecified = true;
        option.DefaultApiVersion = new ApiVersion(2);
        option.ReportApiVersions = true;
        option.ApiVersionReader = ApiVersionReader.Combine(
            new UrlSegmentApiVersionReader(),
            new QueryStringApiVersionReader("api-version"),
            new HeaderApiVersionReader("X-Version"),
            new MediaTypeApiVersionReader("x-version"));
    })
    .AddMvc(options =>
    {
        options.Conventions.Add(new VersionByNamespaceConvention());
    })
    .AddApiExplorer(options =>
    {
        options.GroupNameFormat = "'v'V";
        options.SubstituteApiVersionInUrl = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.ConfigureOptions<NamedSwaggerGenOptions>();
builder.Services.AddSwaggerGenNewtonsoftSupport().AddSwaggerGen(opt =>
{
    string xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    opt.IncludeXmlComments(Path.Combine(AppContext.BaseDirectory, xmlFilename));
});

log.Debug("Registering services...");
builder.Services
    .AddKenkuDatabase()
    .AddKenkuConnectors()
    .AddMetadataFetchers(settings)
    .AddChapterAcquirers()
    .AddMetadataResolvers()
    .AddKenkuServices()
    .AddJobRuntime(settings)
    .AddReconcilers()
    .AddTorrentAcquisitionPath(settings, log);

builder.Services.AddSingleton<RateLimitHandler>();
builder.Services.AddSingleton<IHttpRequester, HttpRequester>();

builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});
builder.Services.AddControllers(options =>
{
    options.AllowEmptyInputInBodyModelBinding = true;
}).AddNewtonsoftJson(opts =>
{
    opts.SerializerSettings.Converters.Add(new StringEnumConverter());
});
builder.Services.AddScoped<ILog>(_ => LogManager.GetLogger("API"));

builder.WebHost.UseUrls($"http://*:{settings.Port}");

log.Info("Starting app...");
WebApplication app = builder.Build();

app.UseCors("AllowAll");

// Serve the bundled frontend (prerendered Nuxt SPA copied into wwwroot at image-build time).
// Static assets and the API share this origin; client-side deep links fall back to the SPA entry below.
app.UseDefaultFiles();
app.UseStaticFiles();

ApiVersionSet apiVersionSet = app.NewApiVersionSet()
    .HasApiVersion(new ApiVersion(2))
    .ReportApiVersions()
    .Build();

log.Debug("Mapping Controllers...");
app.MapControllers()
    .WithApiVersionSet(apiVersionSet)
    .MapToApiVersion(2);

// SPA fallback: any unmatched, non-file, non-API route returns the Nuxt SPA entry document so
// client-side routes (e.g. /series/{id}) resolve on hard refresh / deep link. Lowest route priority,
// so it never shadows controller endpoints or existing static files.
app.MapFallbackToFile("200.html");

log.Debug("Adding Swagger...");
app.UseSwagger(opts =>
{
    opts.OpenApiVersion = OpenApiSpecVersion.OpenApi3_0;
    opts.RouteTemplate = "swagger/{documentName}/swagger.json";
});
app.UseSwaggerUI(opts =>
{
    opts.SwaggerEndpoint("/swagger/v2/swagger.json", "v2");
});

app.UseHttpsRedirection();

// Production startup: migrate the DB, seed defaults, and start the workers. Integration tests host the
// app with Kenku:RunStartup=false so they can use an in-memory DB and drive workers explicitly, instead
// of auto-running every worker (and hitting the real network) on boot.
if (builder.Configuration.GetValue("Kenku:RunStartup", true)
    && !await StartupTasks.MigrateAndSeedAsync(app, settings, log))
    return;

log.Info("Running app.");
await app.RunAsync();

// Exposed so WebApplicationFactory<Program> can host the real app in integration tests.
public partial class Program;
