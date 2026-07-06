using Microsoft.Extensions.Options;
using renegotiation_service.Adapters.Inbound.Http;
using renegotiation_service.Adapters.Outbound.Http;
using renegotiation_service.Application.Ports.Inbound;
using renegotiation_service.Application.Ports.Outbound;
using renegotiation_service.Application.UseCases;
using renegotiation_service.Configuration;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Attach distributed-trace IDs to every log scope and render scopes in console output,
// so a single request can be correlated across an endpoint and its underlying HTTP client call.
builder.Logging.Configure(options =>
{
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId
        | ActivityTrackingOptions.SpanId
        | ActivityTrackingOptions.ParentId;
});
builder.Logging.AddSimpleConsole(options => options.IncludeScopes = true);

builder.Services.AddOptions<ClientApiOptions>()
    .Bind(builder.Configuration.GetSection(ClientApiOptions.SectionName));
builder.Services.AddOptions<EligibilityApiOptions>()
    .Bind(builder.Configuration.GetSection(EligibilityApiOptions.SectionName));
builder.Services.AddOptions<ContractingApiOptions>()
    .Bind(builder.Configuration.GetSection(ContractingApiOptions.SectionName));
builder.Services.AddOptions<FormalizationApiOptions>()
    .Bind(builder.Configuration.GetSection(FormalizationApiOptions.SectionName));

var clientApiRetryAttempts = builder.Configuration.GetValue($"{ClientApiOptions.SectionName}:RetryAttempts", 2);
var eligibilityApiRetryAttempts = builder.Configuration.GetValue($"{EligibilityApiOptions.SectionName}:RetryAttempts", 2);
var contractingApiRetryAttempts = builder.Configuration.GetValue($"{ContractingApiOptions.SectionName}:RetryAttempts", 2);
var formalizationApiRetryAttempts = builder.Configuration.GetValue($"{FormalizationApiOptions.SectionName}:RetryAttempts", 2);

builder.Services.AddHttpClient<IClientApiClient, ClientApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ClientApiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = clientApiRetryAttempts;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });

builder.Services.AddHttpClient<IEligibilityApiClient, EligibilityApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<EligibilityApiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = eligibilityApiRetryAttempts;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });

builder.Services.AddHttpClient<IContractingApiClient, ContractingApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<ContractingApiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = contractingApiRetryAttempts;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });

builder.Services.AddHttpClient<IFormalizationApiClient, FormalizationApiClient>((sp, client) =>
    {
        var options = sp.GetRequiredService<IOptions<FormalizationApiOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = formalizationApiRetryAttempts;
        options.Retry.Delay = TimeSpan.FromMilliseconds(200);
    });

builder.Services.AddScoped<IGetClientUseCase, GetClientUseCase>();
builder.Services.AddScoped<IGetContractsUseCase, GetContractsUseCase>();
builder.Services.AddScoped<IGetDebtsUseCase, GetDebtsUseCase>();
builder.Services.AddScoped<IGetEligibilityUseCase, GetEligibilityUseCase>();
builder.Services.AddScoped<ISimulateUseCase, SimulateUseCase>();
builder.Services.AddScoped<IConfirmAgreementUseCase, ConfirmAgreementUseCase>();
builder.Services.AddScoped<IGetDocumentUseCase, GetDocumentUseCase>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.Use(async (context, next) =>
{
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("CorrelationId");
    var correlationId = Guid.NewGuid().ToString("n");
    using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
    {
        await next(context);
    }
});

app.MapClientLookupEndpoints();
app.MapEligibilityEndpoints();
app.MapSimulationEndpoints();
app.MapFormalizationEndpoints();

app.Run();

public partial class Program;
