using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using renegotiation_service.Adapters.Inbound.Http;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Adapters.Outbound.Persistence;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Application.UseCases;
using renegotiation_service.Configuration;
using renegotiation_service.Platform;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddPlatform(builder.Configuration);

builder.Logging.Configure(options => options.ActivityTrackingOptions =
    ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId | ActivityTrackingOptions.ParentId);
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

builder.Services.AddOptions<ClientApiOptions>().Bind(builder.Configuration.GetSection(ClientApiOptions.SectionName));
builder.Services.AddOptions<EligibilityApiOptions>().Bind(builder.Configuration.GetSection(EligibilityApiOptions.SectionName));
builder.Services.AddOptions<ContractingApiOptions>().Bind(builder.Configuration.GetSection(ContractingApiOptions.SectionName));
builder.Services.AddOptions<FormalizationApiOptions>().Bind(builder.Configuration.GetSection(FormalizationApiOptions.SectionName));
builder.Services.AddOptions<OtelOptions>().Bind(builder.Configuration.GetSection(OtelOptions.SectionName));
builder.Services.AddOptions<PostgresOptions>().Bind(builder.Configuration.GetSection(PostgresOptions.SectionName));

var otelEndpoint = builder.Configuration.GetSection(OtelOptions.SectionName).Get<OtelOptions>()?.OtlpEndpoint
    ?? "http://localhost:4317";
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("renegotiation-service"))
    .WithTracing(tracing => tracing
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddNpgsql()
        .AddOtlpExporter(otlp => otlp.Endpoint = new Uri(otelEndpoint)));

builder.Services.AddSingleton<NpgsqlDataSource>(sp =>
{
    var options = sp.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var connectionString = new NpgsqlConnectionStringBuilder(options.ConnectionString)
    {
        Timeout = 5,
        CommandTimeout = 5
    };
    return new NpgsqlDataSourceBuilder(connectionString.ConnectionString).Build();
});
builder.Services.AddSingleton<ISimulationIdempotencyStore, PostgresSimulationIdempotencyStore>();

static void AddCoreClient<TClient, TImplementation, TOptions>(
    IServiceCollection services,
    int retryAttempts,
    Func<TOptions, string> resolveUrl)
    where TClient : class
    where TImplementation : class, TClient
    where TOptions : class
{
    services.AddHttpClient<TClient, TImplementation>((sp, client) =>
        {
            var options = sp.GetRequiredService<IOptions<TOptions>>().Value;
            client.BaseAddress = new Uri(resolveUrl(options));
        })
        .AddHttpMessageHandler(sp => new InternalRequestHandler(
            sp.GetRequiredService<InternalTokenService>(),
            sp.GetRequiredService<TenantContext>()))
        .AddStandardResilienceHandler(options =>
        {
            options.Retry.MaxRetryAttempts = retryAttempts;
            options.Retry.Delay = TimeSpan.FromMilliseconds(200);
            options.Retry.DisableForUnsafeHttpMethods();
        });
}

AddCoreClient<IClientApiClient, ClientApiClient, ClientApiOptions>(
    builder.Services,
    builder.Configuration.GetValue($"{ClientApiOptions.SectionName}:RetryAttempts", 2),
    options => options.BaseUrl);
AddCoreClient<IEligibilityApiClient, EligibilityApiClient, EligibilityApiOptions>(
    builder.Services,
    builder.Configuration.GetValue($"{EligibilityApiOptions.SectionName}:RetryAttempts", 2),
    options => options.BaseUrl);
AddCoreClient<IContractingApiClient, ContractingApiClient, ContractingApiOptions>(
    builder.Services,
    builder.Configuration.GetValue($"{ContractingApiOptions.SectionName}:RetryAttempts", 2),
    options => options.BaseUrl);
AddCoreClient<IFormalizationApiClient, FormalizationApiClient, FormalizationApiOptions>(
    builder.Services,
    builder.Configuration.GetValue($"{FormalizationApiOptions.SectionName}:RetryAttempts", 2),
    options => options.BaseUrl);

builder.Services.AddScoped<IGetClientUseCase, GetClientUseCase>();
builder.Services.AddScoped<IGetContractsUseCase, GetContractsUseCase>();
builder.Services.AddScoped<IGetDebtsUseCase, GetDebtsUseCase>();
builder.Services.AddScoped<IGetEligibilityUseCase, GetEligibilityUseCase>();
builder.Services.AddScoped<ISimulateUseCase, SimulateUseCase>();
builder.Services.AddScoped<IConfirmAgreementUseCase, ConfirmAgreementUseCase>();
builder.Services.AddScoped<IGetDocumentUseCase, GetDocumentUseCase>();

var app = builder.Build();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UsePlatform();
app.MapGet("/health/ready", async (
    IOptions<InternalAuthOptions> authOptions,
    NpgsqlDataSource dataSource,
    CancellationToken cancellationToken) =>
{
    var failures = new List<string>();
    if (Encoding.UTF8.GetByteCount(authOptions.Value.SigningKey) < 32)
    {
        failures.Add("internal_auth_signing_key_invalid");
    }
    try
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
    catch
    {
        failures.Add("postgres_unavailable");
    }
    return failures.Count == 0
        ? Results.Ok(new { status = "ready", failures })
        : Results.Json(new { status = "not_ready", failures }, statusCode: 503);
}).AllowAnonymous();

app.MapClientLookupEndpoints();
app.MapEligibilityEndpoints();
app.MapSimulationEndpoints();
app.MapFormalizationEndpoints();
app.Run();

public partial class Program;
