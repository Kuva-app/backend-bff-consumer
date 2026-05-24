using System.Diagnostics.Metrics;
using Azure.Identity;
using Kuva.ConsumerBff.Business.Facades;
using Kuva.ConsumerBff.Business.Interfaces;
using Kuva.ConsumerBff.Business.Services;
using Kuva.ConsumerBff.Entities.Constants;
using Kuva.ConsumerBff.Entities.Options;
using Kuva.ConsumerBff.Repository.Context;
using Kuva.ConsumerBff.Repository.Interfaces;
using Kuva.ConsumerBff.Repository.Repositories;
using Kuva.ConsumerBff.Repository.UnitOfWork;
using Kuva.ConsumerBff.Service;
using Kuva.ConsumerBff.Service.Clients;
using Kuva.ConsumerBff.Service.Middlewares;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

var keyVaultUri = builder.Configuration["KeyVault:Uri"];
if (!string.IsNullOrWhiteSpace(keyVaultUri))
{
    builder.Configuration.AddAzureKeyVault(new Uri(keyVaultUri), new DefaultAzureCredential());
}

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.Configure<DownstreamServicesOptions>(builder.Configuration.GetSection("DownstreamServices"));
builder.Services.Configure<ResilienceOptions>(builder.Configuration.GetSection("Resilience"));
builder.Services.Configure<CorsOptions>(builder.Configuration.GetSection("Cors"));
builder.Services.Configure<ObservabilityOptions>(builder.Configuration.GetSection("Observability"));

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ConsumerBffDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("ConsumerBffDatabase") ??
                         "Server=localhost,1435;Database=KuvaConsumerBff;User Id=sa;Password=Change_this_password_123!;Encrypt=True;TrustServerCertificate=True;"));

builder.Services.AddScoped<IIdempotencyKeyRepository, IdempotencyKeyRepository>();
builder.Services.AddScoped<IApiRequestAuditRepository, ApiRequestAuditRepository>();
builder.Services.AddScoped<IConsumerOrderDraftRepository, ConsumerOrderDraftRepository>();
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddScoped<ICurrentConsumerProvider, CurrentConsumerProvider>();
builder.Services.AddSingleton<IConsumerBffMetrics, ConsumerBffMetrics>();
builder.Services.AddScoped<IConsumerStoreFacade, ConsumerStoreFacade>();
builder.Services.AddScoped<IConsumerCatalogFacade, ConsumerCatalogFacade>();
builder.Services.AddScoped<IConsumerOrderFacade, ConsumerOrderFacade>();

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient<IStoreServiceClient, StoreServiceClient>((sp, client) => client.ConfigureDownstream(sp, "Store"));
builder.Services.AddHttpClient<ICatalogPricingServiceClient, CatalogPricingServiceClient>((sp, client) => client.ConfigureDownstream(sp, "CatalogPricing"));
builder.Services.AddHttpClient<IOrderServiceClient, OrderServiceClient>((sp, client) => client.ConfigureDownstream(sp, "Order"));
builder.Services.AddHttpClient<IMediaServiceClient, MediaServiceClient>((sp, client) => client.ConfigureDownstream(sp, "Media"));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.Authority = builder.Configuration["Jwt:Issuer"];
        options.Audience = builder.Configuration["Jwt:Audience"];
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = false,
            SignatureValidator = (token, _) => new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(token),
            RoleClaimType = ClaimTypesConstants.Role
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(PolicyConstants.ConsumerOnly, policy => policy.RequireRole("CONSUMER"));
    options.AddPolicy(PolicyConstants.CanCreateOrder, policy => policy.RequireRole("CONSUMER"));
    options.AddPolicy(PolicyConstants.CanReadOwnOrder, policy => policy.RequireRole("CONSUMER"));
    options.AddPolicy(PolicyConstants.CanCancelOwnOrder, policy => policy.RequireRole("CONSUMER"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
});

builder.Services.AddCors(options =>
{
    var origins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddDefaultPolicy(policy =>
    {
        if (origins.Length == 0)
        {
            policy.AllowAnyOrigin();
        }
        else
        {
            policy.WithOrigins(origins);
        }

        policy.AllowAnyHeader().AllowAnyMethod();
    });
});

builder.Services.AddHealthChecks().AddDbContextCheck<ConsumerBffDbContext>("sqlserver");
builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("kuva-consumer-bff"))
    .WithMetrics(metrics => metrics.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation().AddRuntimeInstrumentation().AddMeter(ConsumerBffMetrics.MeterName))
    .WithTracing(tracing => tracing.AddAspNetCoreInstrumentation().AddHttpClientInstrumentation());

var app = builder.Build();

app.UseMiddleware<CorrelationIdMiddleware>();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseMiddleware<SensitiveDataSanitizationMiddleware>();

app.UseSwagger();
app.UseSwaggerUI();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHealthChecks("/health");
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapGet("/metrics", (IConsumerBffMetrics metrics) => Results.Text(((ConsumerBffMetrics)metrics).RenderPrometheus(), "text/plain"));
app.Run();

public partial class Program
{
}

internal static class HttpClientConfiguration
{
    public static void ConfigureDownstream(this HttpClient client, IServiceProvider services, string name)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var timeout = configuration.GetValue("Resilience:TimeoutSeconds", 10);
        client.BaseAddress = new Uri(configuration[$"DownstreamServices:{name}:BaseUrl"] ?? "http://localhost");
        client.Timeout = TimeSpan.FromSeconds(timeout);
    }
}

public sealed class ConsumerBffMetrics : IConsumerBffMetrics
{
    public const string MeterName = "Kuva.ConsumerBff";
    private readonly Counter<long> _ordersCreated;
    private readonly Counter<long> _ordersConfirmed;
    private readonly Counter<long> _uploadSessionsCreated;
    private readonly Counter<long> _orderConfirmFailures;
    private readonly Counter<long> _downstreamFailures;
    private readonly Counter<long> _idempotencyReplays;
    private long _ordersCreatedValue;
    private long _ordersConfirmedValue;
    private long _uploadSessionsCreatedValue;
    private long _orderConfirmFailuresValue;
    private long _downstreamFailuresValue;
    private long _idempotencyReplaysValue;

    public ConsumerBffMetrics()
    {
        var meter = new Meter(MeterName);
        _ordersCreated = meter.CreateCounter<long>("kuva_consumer_bff_orders_created_total");
        _ordersConfirmed = meter.CreateCounter<long>("kuva_consumer_bff_orders_confirmed_total");
        _uploadSessionsCreated = meter.CreateCounter<long>("kuva_consumer_bff_upload_sessions_created_total");
        _orderConfirmFailures = meter.CreateCounter<long>("kuva_consumer_bff_order_confirm_failures_total");
        _downstreamFailures = meter.CreateCounter<long>("kuva_consumer_bff_downstream_failures_total");
        _idempotencyReplays = meter.CreateCounter<long>("kuva_consumer_bff_idempotency_replays_total");
    }

    public void OrderCreated(Guid storeId)
    {
        Interlocked.Increment(ref _ordersCreatedValue);
        _ordersCreated.Add(1, new KeyValuePair<string, object?>("store_id", Short(storeId)));
    }

    public void OrderConfirmed(Guid storeId)
    {
        Interlocked.Increment(ref _ordersConfirmedValue);
        _ordersConfirmed.Add(1, new KeyValuePair<string, object?>("store_id", Short(storeId)));
    }

    public void UploadSessionCreated(Guid storeId)
    {
        Interlocked.Increment(ref _uploadSessionsCreatedValue);
        _uploadSessionsCreated.Add(1, new KeyValuePair<string, object?>("store_id", Short(storeId)));
    }

    public void OrderConfirmFailed()
    {
        Interlocked.Increment(ref _orderConfirmFailuresValue);
        _orderConfirmFailures.Add(1);
    }

    public void DownstreamFailure(string serviceName, string operation)
    {
        Interlocked.Increment(ref _downstreamFailuresValue);
        _downstreamFailures.Add(1, new KeyValuePair<string, object?>("service_name", serviceName), new KeyValuePair<string, object?>("operation", operation));
    }

    public void IdempotencyReplay()
    {
        Interlocked.Increment(ref _idempotencyReplaysValue);
        _idempotencyReplays.Add(1);
    }

    public string RenderPrometheus() => string.Join('\n',
        "# TYPE kuva_consumer_bff_orders_created_total counter",
        $"kuva_consumer_bff_orders_created_total {_ordersCreatedValue}",
        "# TYPE kuva_consumer_bff_orders_confirmed_total counter",
        $"kuva_consumer_bff_orders_confirmed_total {_ordersConfirmedValue}",
        "# TYPE kuva_consumer_bff_upload_sessions_created_total counter",
        $"kuva_consumer_bff_upload_sessions_created_total {_uploadSessionsCreatedValue}",
        "# TYPE kuva_consumer_bff_order_confirm_failures_total counter",
        $"kuva_consumer_bff_order_confirm_failures_total {_orderConfirmFailuresValue}",
        "# TYPE kuva_consumer_bff_downstream_failures_total counter",
        $"kuva_consumer_bff_downstream_failures_total {_downstreamFailuresValue}",
        "# TYPE kuva_consumer_bff_idempotency_replays_total counter",
        $"kuva_consumer_bff_idempotency_replays_total {_idempotencyReplaysValue}",
        string.Empty);

    private static string Short(Guid value) => value.ToString("N")[..8];
}
