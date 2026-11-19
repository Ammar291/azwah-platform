using System.Text.Json;
using System.Text.Json.Serialization;
using FamilyResilience.Api.Admin;
using FamilyResilience.Api.Authorization;
using FamilyResilience.Api.Background;
using FamilyResilience.Api.Cors;
using FamilyResilience.Api.Database;
using FamilyResilience.Api.Errors;
using FamilyResilience.Api.Health;
using FamilyResilience.Api.Hosting;
using FamilyResilience.Api.Identity;
using FamilyResilience.Api.Observability;
using FamilyResilience.Api.OpenApi;
using FamilyResilience.Api.Questionnaires;
using FamilyResilience.Api.RateLimiting;
using FamilyResilience.Api.Requests;
using FamilyResilience.Api.Safeguarding;
using FamilyResilience.Api.SupportResources;
using FamilyResilience.Api.Validation;
using FamilyResilience.Application;
using FamilyResilience.Infrastructure;
using FamilyResilience.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
DemonstrationDatabaseSetting.Apply(builder.Configuration, builder.Environment);

builder.Configuration.AddLocalOpenAiKey(builder.Environment);
builder.AddObservability();
builder.AddTransport();
builder.Services.AddApplication();
builder.Services.AddInfrastructure();
builder.Services.AddExportStorage();
builder.Services.AddApiAuthentication();
builder.Services.AddApiAuthorization();
builder.Services.AddApiCors();
builder.Services.AddApiRateLimiting();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationFilter>();

        // The FluentValidation validators decide what is required, so their camelCase keys are the only validation
        // errors (MVC's implicit [Required] for non-nullable strings would answer first with PascalCase keys).
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;

        // Content-import and instrument-create bodies are read strictly, ahead of MVC's lenient JSON formatter.
        options.InputFormatters.Insert(0, new ContentJsonInputFormatter());

        // Query and route enums by name, in snake case as JSON writes them (?status=in_progress).
        options.ModelBinderProviders.Insert(0, new SnakeCaseEnumModelBinderProvider());
    })
    .AddJsonOptions(options =>
    {
        // Enums travel by name only: a number in place of a name ("decision": 1) is a malformed body.
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false));
        // Parser messages name internal types and byte positions; clients get MVC's generic message, keyed by JSON path.
        options.AllowInputFormatterExceptionMessages = false;
    })
    // Binding errors (for example ?status=unknown) reach ValidationFilter too, so every 400 key is camelCase.
    .ConfigureApiBehaviorOptions(options => options.SuppressModelStateInvalidFilter = true);
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower, allowIntegerValues: false)));
builder.Services.AddApiOpenApi();
builder.Services.AddBackgroundProcessing();

var app = builder.Build();

if (string.IsNullOrWhiteSpace(app.Configuration.GetConnectionString(FamilyResilienceDbContextOptions.ConnectionStringName)))
{
    throw new InvalidOperationException("Connection string 'ConnectionStrings:Default' is not configured.");
}

SecurityConfigurationGuard.ThrowIfInvalid(app.Configuration, app.Environment);
LanguageModelStartupGuard.ThrowIfInvalid(app.Services);
ScoreAlertStartupGuard.ThrowIfInvalid(app.Services);
// Read now, so a mistake in the catalogue stops the API instead of reaching a family (D-114).
_ = app.Services.GetRequiredService<FamilyResilience.Application.Assistant.ISupportKnowledgeGraph>();

var databaseCommand = await DatabaseCommands.TryRunAsync(args, app.Services, app.Environment);
if (databaseCommand is { } exitCode)
{
    return exitCode;
}

if (app.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await DatabaseMigrator.MigrateAsync(app.Services);
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseTransport();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors(CorsSetup.PolicyName);
// The live consultant (Api/Assistant/ConsultantRelay). Callers sign in with a bearer header, which a browser cannot
// attach to a WebSocket, so no ambient credential can be ridden from another origin.
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseAuthentication();
app.UseRateLimiter();
app.UseMiddleware<RoleObservationMiddleware>();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi().AllowAnonymous();
}

app.MapApiHealthChecks();
app.MapControllers();

await app.RunAsync();
return 0;
