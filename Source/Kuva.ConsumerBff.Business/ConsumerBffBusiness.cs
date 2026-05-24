using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Kuva.ConsumerBff.Entities.Contracts.Downstream;
using Kuva.ConsumerBff.Entities.Contracts.Requests;
using Kuva.ConsumerBff.Entities.Contracts.Responses;
using Kuva.ConsumerBff.Entities.Exceptions;

namespace Kuva.ConsumerBff.Business.Interfaces
{

public interface ICurrentConsumerProvider
{
    Guid GetConsumerId();
    string? GetBearerToken();
}

public interface IConsumerBffMetrics
{
    void OrderCreated(Guid storeId);
    void OrderConfirmed(Guid storeId);
    void UploadSessionCreated(Guid storeId);
    void OrderConfirmFailed();
    void DownstreamFailure(string serviceName, string operation);
    void IdempotencyReplay();
}

public interface IIdempotencyService
{
    Task<IdempotencyReplay<T>?> TryGetReplayAsync<T>(Guid consumerId, string key, string requestHash, CancellationToken cancellationToken);
    Task RegisterProcessingAsync(Guid consumerId, string key, string requestHash, CancellationToken cancellationToken);
    Task CompleteAsync<T>(Guid consumerId, string key, string resourceType, Guid resourceId, T response, CancellationToken cancellationToken);
}

public sealed record IdempotencyReplay<T>(T Response);

public interface IStoreServiceClient
{
    Task<IReadOnlyCollection<DownstreamStore>> GetActiveStoresAsync(CancellationToken cancellationToken);
    Task<DownstreamStore> GetStoreAsync(Guid storeId, CancellationToken cancellationToken);
    Task<bool> IsStoreActiveAsync(Guid storeId, CancellationToken cancellationToken);
}

public interface ICatalogPricingServiceClient
{
    Task<DownstreamCatalog> GetCatalogAsync(Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DownstreamSku>> GetSkusAsync(Guid storeId, CancellationToken cancellationToken);
    Task<DownstreamSku> GetSkuAsync(Guid storeId, Guid skuId, CancellationToken cancellationToken);
}

public interface IOrderServiceClient
{
    Task<DownstreamOrder> CreateOrderAsync(DownstreamCreateOrderRequest request, CancellationToken cancellationToken);
    Task<DownstreamOrder> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<DownstreamOrder>> GetOrdersAsync(Guid consumerId, CancellationToken cancellationToken);
    Task<DownstreamOrderStatus> GetStatusAsync(Guid orderId, CancellationToken cancellationToken);
    Task<DownstreamConfirmOrder> ConfirmAsync(Guid orderId, ConfirmConsumerOrderRequest request, CancellationToken cancellationToken);
    Task<DownstreamOrderStatus> CancelAsync(Guid orderId, CancelConsumerOrderRequest request, CancellationToken cancellationToken);
}

public interface IMediaServiceClient
{
    Task<DownstreamUploadSession> CreateUploadSessionAsync(Guid orderId, CreateUploadSessionRequest request, CancellationToken cancellationToken);
    Task<bool> HasValidUploadedPhotosAsync(Guid orderId, CancellationToken cancellationToken);
}

public interface IConsumerStoreFacade
{
    Task<StoreListResponse> GetStoresAsync(CancellationToken cancellationToken);
    Task<StoreResponse> GetStoreAsync(Guid storeId, CancellationToken cancellationToken);
}

public interface IConsumerCatalogFacade
{
    Task<CatalogResponse> GetCatalogAsync(Guid storeId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<SkuResponse>> GetSkusAsync(Guid storeId, CancellationToken cancellationToken);
    Task<SkuResponse> GetSkuAsync(Guid storeId, Guid skuId, CancellationToken cancellationToken);
}

public interface IConsumerOrderFacade
{
    Task<ConsumerOrderResponse> CreateOrderAsync(CreateConsumerOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<UploadSessionResponse> CreateUploadSessionAsync(Guid orderId, CreateUploadSessionRequest request, CancellationToken cancellationToken);
    Task<ConfirmOrderResponse> ConfirmOrderAsync(Guid orderId, ConfirmConsumerOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken);
    Task<OrderStatusResponse> CancelOrderAsync(Guid orderId, CancelConsumerOrderRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<ConsumerOrderResponse>> GetOrdersAsync(CancellationToken cancellationToken);
    Task<ConsumerOrderResponse> GetOrderAsync(Guid orderId, CancellationToken cancellationToken);
    Task<OrderStatusResponse> GetStatusAsync(Guid orderId, CancellationToken cancellationToken);
}
}

namespace Kuva.ConsumerBff.Business.Mappers
{

public static class StoreMapper
{
    public static StoreResponse ToResponse(DownstreamStore store) =>
        new(store.StoreId, store.Name, store.Status, new AddressResponse(store.Address.Street, store.Address.Number, store.Address.City, store.Address.State, store.Address.ZipCode), store.PickupInstructions, store.OpeningHoursSummary);
}

public static class CatalogMapper
{
    public static CatalogResponse ToResponse(DownstreamCatalog catalog) =>
        new(catalog.StoreId, catalog.Products.Select(ToProduct).Where(x => x.Skus.Count > 0).ToArray());

    public static SkuResponse ToSku(DownstreamSku sku) =>
        new(sku.SkuId, sku.Code, sku.Name, sku.Attributes.Select(a => new SkuAttributeResponse(a.Name, a.Value, a.Unit)).ToArray(), sku.UnitPrice, sku.Currency, sku.Available);

    private static ProductResponse ToProduct(DownstreamProduct product) =>
        new(product.ProductId, product.Name, product.Description, product.Skus.Where(x => x.Available).Select(ToSku).ToArray());
}

public static class OrderMapper
{
    public static ConsumerOrderResponse ToResponse(DownstreamOrder order) =>
        new(order.OrderId, order.Status, order.StoreId, order.TotalAmount, order.TotalPhotos, order.Currency, order.Items.Select(x => new ConsumerOrderItemResponse(x.SkuId, x.SkuCode, x.Name, x.UnitPrice, x.Quantity, x.Subtotal)).ToArray());

    public static UploadSessionResponse ToResponse(DownstreamUploadSession session) =>
        new(session.OrderId, session.ExpiresAt, session.Uploads.Select(x => new UploadTargetResponse(x.PhotoId, x.ClientFileId, x.UploadUrl, x.Method, x.Headers)).ToArray());

    public static ConfirmOrderResponse ToResponse(DownstreamConfirmOrder order) =>
        new(order.OrderId, order.Status, order.Message, new PickupResponse(order.Pickup.StoreName, order.Pickup.PickupCode, order.Pickup.Instructions));
}
}

namespace Kuva.ConsumerBff.Business.Validators
{

public static class ConsumerOrderValidators
{
    public static void ValidateCreate(CreateConsumerOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.StoreId == Guid.Empty)
        {
            errors["storeId"] = ["A loja é obrigatória."];
        }

        if (request.Items is null || request.Items.Count == 0)
        {
            errors["items"] = ["O pedido deve possuir pelo menos um item."];
        }
        else if (request.Items.Any(x => x.SkuId == Guid.Empty || x.Quantity <= 0))
        {
            errors["items"] = ["Cada item deve possuir SKU válido e quantidade maior que zero."];
        }

        ThrowIfAny(errors);
    }

    public static void ValidateUpload(CreateUploadSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Photos is null || request.Photos.Count == 0)
        {
            errors["photos"] = ["Informe pelo menos uma foto."];
        }
        else
        {
            var allowed = new[] { "image/jpeg", "image/png", "image/heic", "image/heif" };
            if (request.Photos.Any(x => !allowed.Contains(x.ContentType, StringComparer.OrdinalIgnoreCase)))
            {
                errors["contentType"] = ["Tipo de arquivo não permitido."];
            }

            if (request.Photos.Any(x => x.SizeBytes <= 0 || x.SizeBytes > 25 * 1024 * 1024))
            {
                errors["sizeBytes"] = ["Cada foto deve ter até 25 MB."];
            }
        }

        ThrowIfAny(errors);
    }

    public static void ValidateConfirm(ConfirmConsumerOrderRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (!request.AcceptedTerms)
        {
            errors["acceptedTerms"] = ["Os termos devem ser aceitos."];
        }

        if (!request.AcceptedPhotoProcessingConsent)
        {
            errors["acceptedPhotoProcessingConsent"] = ["O consentimento de processamento de fotos deve ser aceito."];
        }

        ThrowIfAny(errors);
    }

    private static void ThrowIfAny(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }
    }
}
}

namespace Kuva.ConsumerBff.Business.Services
{

using Kuva.ConsumerBff.Business.Interfaces;
using Kuva.ConsumerBff.Repository.Interfaces;

public sealed class IdempotencyService : IIdempotencyService
{
    private readonly IIdempotencyKeyRepository _repository;
    private readonly IUnitOfWork _unitOfWork;

    public IdempotencyService(IIdempotencyKeyRepository repository, IUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<IdempotencyReplay<T>?> TryGetReplayAsync<T>(Guid consumerId, string key, string requestHash, CancellationToken cancellationToken)
    {
        var entity = await _repository.GetAsync(consumerId, key, cancellationToken);
        if (entity is null)
        {
            return null;
        }

        if (!string.Equals(entity.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new BusinessException("IDEMPOTENCY_KEY_CONFLICT", "A chave de idempotência foi reutilizada com payload diferente.");
        }

        if (entity.Status == "Completed" && !string.IsNullOrWhiteSpace(entity.ResponsePayloadJson))
        {
            var response = JsonSerializer.Deserialize<T>(entity.ResponsePayloadJson)!;
            return new IdempotencyReplay<T>(response);
        }

        throw new BusinessException("IDEMPOTENCY_KEY_PROCESSING", "A requisição original ainda está em processamento.");
    }

    public async Task RegisterProcessingAsync(Guid consumerId, string key, string requestHash, CancellationToken cancellationToken)
    {
        await _repository.AddAsync(consumerId, key, requestHash, DateTimeOffset.UtcNow.AddHours(24), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteAsync<T>(Guid consumerId, string key, string resourceType, Guid resourceId, T response, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(response);
        await _repository.CompleteAsync(consumerId, key, resourceType, resourceId, json, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
}

namespace Kuva.ConsumerBff.Business.Facades
{

using Kuva.ConsumerBff.Business.Interfaces;
using Kuva.ConsumerBff.Business.Mappers;
using Kuva.ConsumerBff.Business.Validators;

public sealed class ConsumerStoreFacade : IConsumerStoreFacade
{
    private readonly IStoreServiceClient _storeClient;

    public ConsumerStoreFacade(IStoreServiceClient storeClient)
    {
        _storeClient = storeClient;
    }

    public async Task<StoreListResponse> GetStoresAsync(CancellationToken cancellationToken)
    {
        var stores = await _storeClient.GetActiveStoresAsync(cancellationToken);
        return new StoreListResponse(stores.Where(x => x.Status.Equals("Active", StringComparison.OrdinalIgnoreCase)).Select(StoreMapper.ToResponse).ToArray());
    }

    public async Task<StoreResponse> GetStoreAsync(Guid storeId, CancellationToken cancellationToken)
    {
        var store = await _storeClient.GetStoreAsync(storeId, cancellationToken);
        if (!store.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessException("STORE_INACTIVE", "A loja está inativa no momento.");
        }

        return StoreMapper.ToResponse(store);
    }
}

public sealed class ConsumerCatalogFacade : IConsumerCatalogFacade
{
    private readonly IStoreServiceClient _storeClient;
    private readonly ICatalogPricingServiceClient _catalogClient;

    public ConsumerCatalogFacade(IStoreServiceClient storeClient, ICatalogPricingServiceClient catalogClient)
    {
        _storeClient = storeClient;
        _catalogClient = catalogClient;
    }

    public async Task<CatalogResponse> GetCatalogAsync(Guid storeId, CancellationToken cancellationToken)
    {
        await EnsureStoreActiveAsync(storeId, cancellationToken);
        return CatalogMapper.ToResponse(await _catalogClient.GetCatalogAsync(storeId, cancellationToken));
    }

    public async Task<IReadOnlyCollection<SkuResponse>> GetSkusAsync(Guid storeId, CancellationToken cancellationToken)
    {
        await EnsureStoreActiveAsync(storeId, cancellationToken);
        return (await _catalogClient.GetSkusAsync(storeId, cancellationToken)).Where(x => x.Available).Select(CatalogMapper.ToSku).ToArray();
    }

    public async Task<SkuResponse> GetSkuAsync(Guid storeId, Guid skuId, CancellationToken cancellationToken)
    {
        await EnsureStoreActiveAsync(storeId, cancellationToken);
        var sku = await _catalogClient.GetSkuAsync(storeId, skuId, cancellationToken);
        if (!sku.Available)
        {
            throw new BusinessException("SKU_UNAVAILABLE", "SKU indisponível para a loja.");
        }

        return CatalogMapper.ToSku(sku);
    }

    private async Task EnsureStoreActiveAsync(Guid storeId, CancellationToken cancellationToken)
    {
        if (!await _storeClient.IsStoreActiveAsync(storeId, cancellationToken))
        {
            throw new BusinessException("STORE_INACTIVE", "A loja está inativa no momento.");
        }
    }
}

public sealed class ConsumerOrderFacade : IConsumerOrderFacade
{
    private readonly IStoreServiceClient _storeClient;
    private readonly ICatalogPricingServiceClient _catalogClient;
    private readonly IOrderServiceClient _orderClient;
    private readonly IMediaServiceClient _mediaClient;
    private readonly ICurrentConsumerProvider _currentConsumer;
    private readonly IIdempotencyService _idempotencyService;
    private readonly IConsumerBffMetrics _metrics;

    public ConsumerOrderFacade(IStoreServiceClient storeClient, ICatalogPricingServiceClient catalogClient, IOrderServiceClient orderClient, IMediaServiceClient mediaClient, ICurrentConsumerProvider currentConsumer, IIdempotencyService idempotencyService, IConsumerBffMetrics metrics)
    {
        _storeClient = storeClient;
        _catalogClient = catalogClient;
        _orderClient = orderClient;
        _mediaClient = mediaClient;
        _currentConsumer = currentConsumer;
        _idempotencyService = idempotencyService;
        _metrics = metrics;
    }

    public async Task<ConsumerOrderResponse> CreateOrderAsync(CreateConsumerOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        ConsumerOrderValidators.ValidateCreate(request);
        var consumerId = _currentConsumer.GetConsumerId();
        var hash = Hash(request);

        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await _idempotencyService.TryGetReplayAsync<ConsumerOrderResponse>(consumerId, idempotencyKey, hash, cancellationToken);
            if (replay is not null)
            {
                _metrics.IdempotencyReplay();
                return replay.Response;
            }

            await _idempotencyService.RegisterProcessingAsync(consumerId, idempotencyKey, hash, cancellationToken);
        }

        if (!await _storeClient.IsStoreActiveAsync(request.StoreId, cancellationToken))
        {
            throw new BusinessException("STORE_INACTIVE", "A loja está inativa no momento.");
        }

        var skus = await _catalogClient.GetSkusAsync(request.StoreId, cancellationToken);
        var unavailable = request.Items.Any(i => skus.All(s => s.SkuId != i.SkuId || !s.Available));
        if (unavailable)
        {
            throw new BusinessException("SKU_UNAVAILABLE", "SKU indisponível para a loja.");
        }

        var order = await _orderClient.CreateOrderAsync(new DownstreamCreateOrderRequest(consumerId, request.StoreId, request.Items.Select(x => new DownstreamCreateOrderItem(x.SkuId, x.Quantity)).ToArray()), cancellationToken);
        var response = OrderMapper.ToResponse(order);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await _idempotencyService.CompleteAsync(consumerId, idempotencyKey, "Order", order.OrderId, response, cancellationToken);
        }

        _metrics.OrderCreated(request.StoreId);
        return response;
    }

    public async Task<UploadSessionResponse> CreateUploadSessionAsync(Guid orderId, CreateUploadSessionRequest request, CancellationToken cancellationToken)
    {
        ConsumerOrderValidators.ValidateUpload(request);
        var order = await EnsureOwnOrderAsync(orderId, cancellationToken);
        var session = await _mediaClient.CreateUploadSessionAsync(orderId, request, cancellationToken);
        _metrics.UploadSessionCreated(order.StoreId);
        return OrderMapper.ToResponse(session);
    }

    public async Task<ConfirmOrderResponse> ConfirmOrderAsync(Guid orderId, ConfirmConsumerOrderRequest request, string? idempotencyKey, CancellationToken cancellationToken)
    {
        try
        {
            ConsumerOrderValidators.ValidateConfirm(request);
            var order = await EnsureOwnOrderAsync(orderId, cancellationToken);
            if (!await _mediaClient.HasValidUploadedPhotosAsync(orderId, cancellationToken))
            {
                throw new BusinessException("NO_VALID_PHOTOS", "Nenhuma foto válida foi enviada para o pedido.");
            }

            var confirmed = await _orderClient.ConfirmAsync(orderId, request, cancellationToken);
            _metrics.OrderConfirmed(order.StoreId);
            return OrderMapper.ToResponse(confirmed);
        }
        catch
        {
            _metrics.OrderConfirmFailed();
            throw;
        }
    }

    public async Task<OrderStatusResponse> CancelOrderAsync(Guid orderId, CancelConsumerOrderRequest request, CancellationToken cancellationToken)
    {
        await EnsureOwnOrderAsync(orderId, cancellationToken);
        var status = await _orderClient.CancelAsync(orderId, request, cancellationToken);
        return new OrderStatusResponse(status.OrderId, status.Status);
    }

    public async Task<IReadOnlyCollection<ConsumerOrderResponse>> GetOrdersAsync(CancellationToken cancellationToken)
    {
        var consumerId = _currentConsumer.GetConsumerId();
        return (await _orderClient.GetOrdersAsync(consumerId, cancellationToken)).Where(x => x.ConsumerId == consumerId).Select(OrderMapper.ToResponse).ToArray();
    }

    public async Task<ConsumerOrderResponse> GetOrderAsync(Guid orderId, CancellationToken cancellationToken) =>
        OrderMapper.ToResponse(await EnsureOwnOrderAsync(orderId, cancellationToken));

    public async Task<OrderStatusResponse> GetStatusAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var status = await _orderClient.GetStatusAsync(orderId, cancellationToken);
        if (status.ConsumerId != _currentConsumer.GetConsumerId())
        {
            throw new ForbiddenResourceException();
        }

        return new OrderStatusResponse(status.OrderId, status.Status);
    }

    private async Task<DownstreamOrder> EnsureOwnOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var order = await _orderClient.GetOrderAsync(orderId, cancellationToken);
        if (order.ConsumerId != _currentConsumer.GetConsumerId())
        {
            throw new ForbiddenResourceException();
        }

        return order;
    }

    private static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
}
