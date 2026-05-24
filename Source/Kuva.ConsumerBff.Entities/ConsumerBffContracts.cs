namespace Kuva.ConsumerBff.Entities.Constants
{

public static class HeaderConstants
{
    public const string CorrelationId = "X-Correlation-Id";
    public const string ConsumerId = "X-Consumer-Id";
    public const string RequestSource = "X-Request-Source";
    public const string IdempotencyKey = "Idempotency-Key";
}

public static class ClaimTypesConstants
{
    public const string Subject = "sub";
    public const string Role = "roles";
}

public static class PolicyConstants
{
    public const string ConsumerOnly = "ConsumerOnly";
    public const string CanCreateOrder = "CanCreateOrder";
    public const string CanReadOwnOrder = "CanReadOwnOrder";
    public const string CanCancelOwnOrder = "CanCancelOwnOrder";
}
}

namespace Kuva.ConsumerBff.Entities.Enums
{

public enum ConsumerOrderStatus
{
    Created,
    AwaitingUpload,
    ReceivedByStore,
    InProduction,
    ReadyForPickup,
    Completed,
    Cancelled
}

public enum IdempotencyStatus
{
    Processing,
    Completed,
    Failed,
    Expired
}

public enum RequestAuditStatus
{
    Succeeded,
    Failed
}
}

namespace Kuva.ConsumerBff.Entities.Exceptions
{

public class BusinessException : Exception
{
    public BusinessException(string code, string message, int statusCode = 409)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public sealed class ValidationException : BusinessException
{
    public ValidationException(Dictionary<string, string[]> errors)
        : base("VALIDATION_ERROR", "Revise os dados enviados e tente novamente.", 400)
    {
        Errors = errors;
    }

    public Dictionary<string, string[]> Errors { get; }
}

public sealed class ForbiddenResourceException : BusinessException
{
    public ForbiddenResourceException(string message = "Recurso não pertence ao consumidor autenticado.")
        : base("FORBIDDEN_RESOURCE", message, 403)
    {
    }
}

public sealed class NotFoundResourceException : BusinessException
{
    public NotFoundResourceException(string resource)
        : base("RESOURCE_NOT_FOUND", $"{resource} não encontrado.", 404)
    {
    }
}

public class DownstreamServiceException : BusinessException
{
    public DownstreamServiceException(string serviceName, string operation, string message = "Serviço interno indisponível.", int statusCode = 503)
        : base("DOWNSTREAM_SERVICE_ERROR", message, statusCode)
    {
        ServiceName = serviceName;
        Operation = operation;
    }

    public string ServiceName { get; }
    public string Operation { get; }
}

public sealed class DownstreamTimeoutException : DownstreamServiceException
{
    public DownstreamTimeoutException(string serviceName, string operation)
        : base(serviceName, operation, "Tempo esgotado ao chamar serviço interno.", 504)
    {
    }
}
}

namespace Kuva.ConsumerBff.Entities.Models
{

public sealed record AuthenticatedConsumer(Guid ConsumerId, IReadOnlyCollection<string> Roles);
public sealed record CorrelationContext(string CorrelationId);
public sealed record PagedResult<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalItems);
}

namespace Kuva.ConsumerBff.Entities.Options
{

public sealed class AuthOptions
{
    public string Issuer { get; set; } = "kuva-auth";
    public string Audience { get; set; } = "kuva-consumer-bff";
    public string? JwksUrl { get; set; }
}

public sealed class DownstreamServicesOptions
{
    public DownstreamServiceOptions Auth { get; set; } = new();
    public DownstreamServiceOptions Store { get; set; } = new();
    public DownstreamServiceOptions CatalogPricing { get; set; } = new();
    public DownstreamServiceOptions Order { get; set; } = new();
    public DownstreamServiceOptions Media { get; set; } = new();
}

public sealed class DownstreamServiceOptions
{
    public string BaseUrl { get; set; } = "http://localhost";
}

public sealed class ResilienceOptions
{
    public int TimeoutSeconds { get; set; } = 10;
    public int RetryCount { get; set; } = 2;
    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakerDurationSeconds { get; set; } = 30;
}

public sealed class ObservabilityOptions
{
    public string ServiceName { get; set; } = "kuva-consumer-bff";
    public bool PrometheusEnabled { get; set; } = true;
}

public sealed class DatabaseOptions
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class CorsOptions
{
    public string[] AllowedOrigins { get; set; } = [];
}
}

namespace Kuva.ConsumerBff.Entities.Contracts.Requests
{

public sealed record CreateConsumerOrderRequest(Guid StoreId, IReadOnlyCollection<CreateConsumerOrderItemRequest> Items);
public sealed record CreateConsumerOrderItemRequest(Guid SkuId, int Quantity);
public sealed record CreateUploadSessionRequest(IReadOnlyCollection<UploadPhotoRequest> Photos);
public sealed record UploadPhotoRequest(string ClientFileId, string FileName, string ContentType, long SizeBytes, string Checksum);
public sealed record ConfirmConsumerOrderRequest(bool AcceptedTerms, bool AcceptedPhotoProcessingConsent);
public sealed record CancelConsumerOrderRequest(string? Reason);
}

namespace Kuva.ConsumerBff.Entities.Contracts.Responses
{

public sealed record StoreListResponse(IReadOnlyCollection<StoreResponse> Items);
public sealed record StoreResponse(Guid StoreId, string Name, string Status, AddressResponse Address, string PickupInstructions, string OpeningHoursSummary);
public sealed record AddressResponse(string Street, string Number, string City, string State, string ZipCode);
public sealed record CatalogResponse(Guid StoreId, IReadOnlyCollection<ProductResponse> Products);
public sealed record ProductResponse(Guid ProductId, string Name, string Description, IReadOnlyCollection<SkuResponse> Skus);
public sealed record SkuResponse(Guid SkuId, string Code, string Name, IReadOnlyCollection<SkuAttributeResponse> Attributes, decimal UnitPrice, string Currency, bool Available);
public sealed record SkuAttributeResponse(string Name, string Value, string? Unit);
public sealed record ConsumerOrderResponse(Guid OrderId, string Status, Guid StoreId, decimal TotalAmount, int TotalPhotos, string Currency, IReadOnlyCollection<ConsumerOrderItemResponse> Items);
public sealed record ConsumerOrderItemResponse(Guid SkuId, string SkuCode, string Name, decimal UnitPrice, int Quantity, decimal Subtotal);
public sealed record UploadSessionResponse(Guid OrderId, DateTimeOffset ExpiresAt, IReadOnlyCollection<UploadTargetResponse> Uploads);
public sealed record UploadTargetResponse(Guid PhotoId, string ClientFileId, string UploadUrl, string Method, IReadOnlyDictionary<string, string> Headers);
public sealed record ConfirmOrderResponse(Guid OrderId, string Status, string Message, PickupResponse Pickup);
public sealed record PickupResponse(string StoreName, string PickupCode, string Instructions);
public sealed record OrderStatusResponse(Guid OrderId, string Status);
}

namespace Kuva.ConsumerBff.Entities.Contracts.Downstream
{

public sealed record DownstreamStore(Guid StoreId, string Name, string Status, DownstreamAddress Address, string PickupInstructions, string OpeningHoursSummary);
public sealed record DownstreamAddress(string Street, string Number, string City, string State, string ZipCode);
public sealed record DownstreamCatalog(Guid StoreId, IReadOnlyCollection<DownstreamProduct> Products);
public sealed record DownstreamProduct(Guid ProductId, string Name, string Description, IReadOnlyCollection<DownstreamSku> Skus);
public sealed record DownstreamSku(Guid SkuId, string Code, string Name, IReadOnlyCollection<DownstreamSkuAttribute> Attributes, decimal UnitPrice, string Currency, bool Available);
public sealed record DownstreamSkuAttribute(string Name, string Value, string? Unit);
public sealed record DownstreamCreateOrderRequest(Guid ConsumerId, Guid StoreId, IReadOnlyCollection<DownstreamCreateOrderItem> Items);
public sealed record DownstreamCreateOrderItem(Guid SkuId, int Quantity);
public sealed record DownstreamOrder(Guid OrderId, Guid ConsumerId, string Status, Guid StoreId, decimal TotalAmount, int TotalPhotos, string Currency, IReadOnlyCollection<DownstreamOrderItem> Items);
public sealed record DownstreamOrderItem(Guid SkuId, string SkuCode, string Name, decimal UnitPrice, int Quantity, decimal Subtotal);
public sealed record DownstreamUploadSession(Guid OrderId, DateTimeOffset ExpiresAt, IReadOnlyCollection<DownstreamUploadTarget> Uploads);
public sealed record DownstreamUploadTarget(Guid PhotoId, string ClientFileId, string UploadUrl, string Method, IReadOnlyDictionary<string, string> Headers);
public sealed record DownstreamConfirmOrder(Guid OrderId, string Status, string Message, DownstreamPickup Pickup);
public sealed record DownstreamPickup(string StoreName, string PickupCode, string Instructions);
public sealed record DownstreamOrderStatus(Guid OrderId, string Status, Guid ConsumerId);
}
