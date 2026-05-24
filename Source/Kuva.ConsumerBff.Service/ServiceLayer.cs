using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kuva.ConsumerBff.Business.Interfaces;
using Kuva.ConsumerBff.Entities.Constants;
using Kuva.ConsumerBff.Entities.Contracts.Downstream;
using Kuva.ConsumerBff.Entities.Contracts.Requests;
using Kuva.ConsumerBff.Entities.Contracts.Responses;
using Kuva.ConsumerBff.Entities.Exceptions;
using Kuva.ConsumerBff.Repository.Entities;
using Kuva.ConsumerBff.Repository.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kuva.ConsumerBff.Service.Clients
{

public abstract class DownstreamClientBase
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IConsumerBffMetrics _metrics;
    protected readonly HttpClient HttpClient;

    protected DownstreamClientBase(HttpClient httpClient, IHttpContextAccessor httpContextAccessor, IConsumerBffMetrics metrics)
    {
        HttpClient = httpClient;
        _httpContextAccessor = httpContextAccessor;
        _metrics = metrics;
    }

    protected async Task<T> SendAsync<T>(HttpMethod method, string template, object? body, string service, string operation, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, template);
        var context = _httpContextAccessor.HttpContext;
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (context?.Request.Headers.Authorization.ToString() is { Length: > 0 } authorization)
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorization);
        }

        request.Headers.TryAddWithoutValidation(HeaderConstants.CorrelationId, context?.Items[HeaderConstants.CorrelationId]?.ToString() ?? Guid.NewGuid().ToString());
        request.Headers.TryAddWithoutValidation(HeaderConstants.RequestSource, "consumer-bff");
        if (context?.User.FindFirstValue(ClaimTypesConstants.Subject) is { Length: > 0 } consumerId)
        {
            request.Headers.TryAddWithoutValidation(HeaderConstants.ConsumerId, consumerId);
        }

        try
        {
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<T>(cancellationToken);
                return result ?? throw new DownstreamServiceException(service, operation, "Resposta vazia do serviço interno.");
            }

            _metrics.DownstreamFailure(service, operation);
            throw response.StatusCode switch
            {
                HttpStatusCode.NotFound => new NotFoundResourceException(operation),
                HttpStatusCode.Conflict => new BusinessException("DOWNSTREAM_CONFLICT", "Operação recusada pelo serviço interno."),
                HttpStatusCode.Forbidden => new ForbiddenResourceException(),
                _ => new DownstreamServiceException(service, operation)
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _metrics.DownstreamFailure(service, operation);
            throw new DownstreamTimeoutException(service, operation);
        }
    }
}

public sealed class StoreServiceClient : DownstreamClientBase, IStoreServiceClient
{
    public StoreServiceClient(HttpClient httpClient, IHttpContextAccessor accessor, IConsumerBffMetrics metrics)
        : base(httpClient, accessor, metrics)
    {
    }

    public Task<IReadOnlyCollection<DownstreamStore>> GetActiveStoresAsync(CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<DownstreamStore>>(HttpMethod.Get, "/api/internal/stores?status=Active", null, "StoreService", "GetActiveStores", cancellationToken);

    public Task<DownstreamStore> GetStoreAsync(Guid storeId, CancellationToken cancellationToken) =>
        SendAsync<DownstreamStore>(HttpMethod.Get, $"/api/internal/stores/{storeId}", null, "StoreService", "GetStore", cancellationToken);

    public async Task<bool> IsStoreActiveAsync(Guid storeId, CancellationToken cancellationToken) =>
        (await GetStoreAsync(storeId, cancellationToken)).Status.Equals("Active", StringComparison.OrdinalIgnoreCase);
}

public sealed class CatalogPricingServiceClient : DownstreamClientBase, ICatalogPricingServiceClient
{
    public CatalogPricingServiceClient(HttpClient httpClient, IHttpContextAccessor accessor, IConsumerBffMetrics metrics)
        : base(httpClient, accessor, metrics)
    {
    }

    public Task<DownstreamCatalog> GetCatalogAsync(Guid storeId, CancellationToken cancellationToken) =>
        SendAsync<DownstreamCatalog>(HttpMethod.Get, $"/api/internal/stores/{storeId}/catalog", null, "CatalogPricingService", "GetCatalog", cancellationToken);

    public Task<IReadOnlyCollection<DownstreamSku>> GetSkusAsync(Guid storeId, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<DownstreamSku>>(HttpMethod.Get, $"/api/internal/stores/{storeId}/skus", null, "CatalogPricingService", "GetSkus", cancellationToken);

    public Task<DownstreamSku> GetSkuAsync(Guid storeId, Guid skuId, CancellationToken cancellationToken) =>
        SendAsync<DownstreamSku>(HttpMethod.Get, $"/api/internal/stores/{storeId}/skus/{skuId}", null, "CatalogPricingService", "GetSku", cancellationToken);
}

public sealed class OrderServiceClient : DownstreamClientBase, IOrderServiceClient
{
    public OrderServiceClient(HttpClient httpClient, IHttpContextAccessor accessor, IConsumerBffMetrics metrics)
        : base(httpClient, accessor, metrics)
    {
    }

    public Task<DownstreamOrder> CreateOrderAsync(DownstreamCreateOrderRequest request, CancellationToken cancellationToken) =>
        SendAsync<DownstreamOrder>(HttpMethod.Post, "/api/internal/orders", request, "OrderService", "CreateOrder", cancellationToken);

    public Task<DownstreamOrder> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<DownstreamOrder>(HttpMethod.Get, $"/api/internal/orders/{orderId}", null, "OrderService", "GetOrder", cancellationToken);

    public Task<IReadOnlyCollection<DownstreamOrder>> GetOrdersAsync(Guid consumerId, CancellationToken cancellationToken) =>
        SendAsync<IReadOnlyCollection<DownstreamOrder>>(HttpMethod.Get, $"/api/internal/consumers/{consumerId}/orders", null, "OrderService", "GetOrders", cancellationToken);

    public Task<DownstreamOrderStatus> GetStatusAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<DownstreamOrderStatus>(HttpMethod.Get, $"/api/internal/orders/{orderId}/status", null, "OrderService", "GetStatus", cancellationToken);

    public Task<DownstreamConfirmOrder> ConfirmAsync(Guid orderId, ConfirmConsumerOrderRequest request, CancellationToken cancellationToken) =>
        SendAsync<DownstreamConfirmOrder>(HttpMethod.Post, $"/api/internal/orders/{orderId}/confirm", request, "OrderService", "ConfirmOrder", cancellationToken);

    public Task<DownstreamOrderStatus> CancelAsync(Guid orderId, CancelConsumerOrderRequest request, CancellationToken cancellationToken) =>
        SendAsync<DownstreamOrderStatus>(HttpMethod.Post, $"/api/internal/orders/{orderId}/cancel", request, "OrderService", "CancelOrder", cancellationToken);
}

public sealed class MediaServiceClient : DownstreamClientBase, IMediaServiceClient
{
    public MediaServiceClient(HttpClient httpClient, IHttpContextAccessor accessor, IConsumerBffMetrics metrics)
        : base(httpClient, accessor, metrics)
    {
    }

    public Task<DownstreamUploadSession> CreateUploadSessionAsync(Guid orderId, CreateUploadSessionRequest request, CancellationToken cancellationToken) =>
        SendAsync<DownstreamUploadSession>(HttpMethod.Post, $"/api/internal/orders/{orderId}/upload-session", request, "MediaService", "CreateUploadSession", cancellationToken);

    public Task<bool> HasValidUploadedPhotosAsync(Guid orderId, CancellationToken cancellationToken) =>
        SendAsync<bool>(HttpMethod.Get, $"/api/internal/orders/{orderId}/photos/valid", null, "MediaService", "ValidatePhotos", cancellationToken);
}
}

namespace Kuva.ConsumerBff.Service.Middlewares
{

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers[HeaderConstants.CorrelationId].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(correlationId))
        {
            correlationId = Guid.NewGuid().ToString();
        }

        context.Items[HeaderConstants.CorrelationId] = correlationId;
        context.Response.Headers[HeaderConstants.CorrelationId] = correlationId;
        await _next(context);
    }
}

public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;

    public SecurityHeadersMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
        await _next(context);
    }
}

public sealed class SensitiveDataSanitizationMiddleware
{
    private readonly RequestDelegate _next;

    public SensitiveDataSanitizationMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        context.Request.Headers.Remove("Cookie");
        await _next(context);
    }

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value.Replace("Authorization", "[redacted]", StringComparison.OrdinalIgnoreCase)
            .Replace("sig=", "sig=[redacted]", StringComparison.OrdinalIgnoreCase);
        return sanitized.Length > 1000 ? sanitized[..1000] : sanitized;
    }
}

public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;

    public ExceptionHandlingMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException ex)
        {
            await WriteProblemAsync(context, ex.StatusCode, "Dados inválidos", ex.Message, "https://kuva.com.br/errors/validation-error", ex.Errors);
        }
        catch (BusinessException ex)
        {
            await WriteProblemAsync(context, ex.StatusCode, "Operação não concluída", ex.Message, $"https://kuva.com.br/errors/{ex.Code.ToLowerInvariant()}");
        }
        catch (Exception)
        {
            await WriteProblemAsync(context, 500, "Erro inesperado", "Não foi possível concluir a operação.", "https://kuva.com.br/errors/unexpected-error");
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int status, string title, string detail, string type, Dictionary<string, string[]>? errors = null)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Type = type, Instance = context.Request.Path };
        problem.Extensions["traceId"] = context.Items[HeaderConstants.CorrelationId]?.ToString() ?? context.TraceIdentifier;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        await context.Response.WriteAsJsonAsync(problem);
    }
}
}

namespace Kuva.ConsumerBff.Service
{

public sealed class CurrentConsumerProvider : ICurrentConsumerProvider
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentConsumerProvider(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public Guid GetConsumerId()
    {
        var value = _accessor.HttpContext?.User.FindFirstValue(ClaimTypesConstants.Subject) ??
                    _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var consumerId))
        {
            throw new BusinessException("INVALID_CONSUMER", "Token sem consumidor válido.", 401);
        }

        return consumerId;
    }

    public string? GetBearerToken() => _accessor.HttpContext?.Request.Headers.Authorization.ToString();
}
}

namespace Kuva.ConsumerBff.Service.Controllers
{

[ApiController]
[AllowAnonymous]
[Route("api/v1/consumer/stores")]
public sealed class ConsumerStoresController : ControllerBase
{
    private readonly IConsumerStoreFacade _facade;

    public ConsumerStoresController(IConsumerStoreFacade facade)
    {
        _facade = facade;
    }

    [HttpGet]
    public async Task<ActionResult<StoreListResponse>> GetStores(CancellationToken cancellationToken) =>
        Ok(await _facade.GetStoresAsync(cancellationToken));

    [HttpGet("{storeId:guid}")]
    public async Task<ActionResult<StoreResponse>> GetStore(Guid storeId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetStoreAsync(storeId, cancellationToken));
}

[ApiController]
[AllowAnonymous]
[Route("api/v1/consumer/stores/{storeId:guid}")]
public sealed class ConsumerCatalogController : ControllerBase
{
    private readonly IConsumerCatalogFacade _facade;

    public ConsumerCatalogController(IConsumerCatalogFacade facade)
    {
        _facade = facade;
    }

    [HttpGet("catalog")]
    public async Task<ActionResult<CatalogResponse>> GetCatalog(Guid storeId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetCatalogAsync(storeId, cancellationToken));

    [HttpGet("skus")]
    public async Task<ActionResult<IReadOnlyCollection<SkuResponse>>> GetSkus(Guid storeId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetSkusAsync(storeId, cancellationToken));

    [HttpGet("skus/{skuId:guid}")]
    public async Task<ActionResult<SkuResponse>> GetSku(Guid storeId, Guid skuId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetSkuAsync(storeId, skuId, cancellationToken));
}

[ApiController]
[Authorize(Policy = PolicyConstants.ConsumerOnly)]
[Route("api/v1/consumer/orders")]
public sealed class ConsumerOrdersController : ControllerBase
{
    private readonly IConsumerOrderFacade _facade;

    public ConsumerOrdersController(IConsumerOrderFacade facade)
    {
        _facade = facade;
    }

    [HttpPost]
    public async Task<ActionResult<ConsumerOrderResponse>> Create(CreateConsumerOrderRequest request, CancellationToken cancellationToken)
    {
        var response = await _facade.CreateOrderAsync(request, Request.Headers[HeaderConstants.IdempotencyKey], cancellationToken);
        return CreatedAtAction(nameof(GetOrder), new { orderId = response.OrderId }, response);
    }

    [HttpPost("{orderId:guid}/upload-session")]
    public async Task<ActionResult<UploadSessionResponse>> CreateUploadSession(Guid orderId, CreateUploadSessionRequest request, CancellationToken cancellationToken) =>
        Ok(await _facade.CreateUploadSessionAsync(orderId, request, cancellationToken));

    [HttpPost("{orderId:guid}/confirm")]
    public async Task<ActionResult<ConfirmOrderResponse>> Confirm(Guid orderId, ConfirmConsumerOrderRequest request, CancellationToken cancellationToken) =>
        Ok(await _facade.ConfirmOrderAsync(orderId, request, Request.Headers[HeaderConstants.IdempotencyKey], cancellationToken));

    [HttpPost("{orderId:guid}/cancel")]
    public async Task<ActionResult<OrderStatusResponse>> Cancel(Guid orderId, CancelConsumerOrderRequest request, CancellationToken cancellationToken) =>
        Ok(await _facade.CancelOrderAsync(orderId, request, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<IReadOnlyCollection<ConsumerOrderResponse>>> GetOrders(CancellationToken cancellationToken) =>
        Ok(await _facade.GetOrdersAsync(cancellationToken));

    [HttpGet("{orderId:guid}")]
    public async Task<ActionResult<ConsumerOrderResponse>> GetOrder(Guid orderId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetOrderAsync(orderId, cancellationToken));

    [HttpGet("{orderId:guid}/status")]
    public async Task<ActionResult<OrderStatusResponse>> GetStatus(Guid orderId, CancellationToken cancellationToken) =>
        Ok(await _facade.GetStatusAsync(orderId, cancellationToken));
}
}

namespace Kuva.ConsumerBff.Service.Middlewares
{

public sealed class RequestAuditMiddleware
{
    private readonly RequestDelegate _next;

    public RequestAuditMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, IApiRequestAuditRepository repository, IUnitOfWork unitOfWork)
    {
        var started = DateTimeOffset.UtcNow;
        await _next(context);
        var ip = context.Connection.RemoteIpAddress?.ToString();
        var ua = context.Request.Headers.UserAgent.ToString();
        await repository.AddAsync(new ApiRequestAuditEntity
        {
            ConsumerId = Guid.TryParse(context.User.FindFirstValue(ClaimTypesConstants.Subject), out var id) ? id : null,
            CorrelationId = context.Items[HeaderConstants.CorrelationId]?.ToString() ?? context.TraceIdentifier,
            Method = context.Request.Method,
            Path = context.Request.Path,
            StatusCode = context.Response.StatusCode,
            ElapsedMs = (int)(DateTimeOffset.UtcNow - started).TotalMilliseconds,
            ClientAppVersion = context.Request.Headers["X-App-Version"],
            DevicePlatform = context.Request.Headers["X-Device-Platform"],
            IpHash = Hash(ip),
            UserAgentHash = Hash(ua)
        }, context.RequestAborted);
        await unitOfWork.SaveChangesAsync(context.RequestAborted);
    }

    private static string? Hash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
}
