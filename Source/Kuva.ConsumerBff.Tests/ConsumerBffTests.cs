using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Kuva.ConsumerBff.Business.Facades;
using Kuva.ConsumerBff.Business.Interfaces;
using Kuva.ConsumerBff.Business.Services;
using Kuva.ConsumerBff.Business.Validators;
using Kuva.ConsumerBff.Entities.Constants;
using Kuva.ConsumerBff.Entities.Contracts.Downstream;
using Kuva.ConsumerBff.Entities.Contracts.Requests;
using Kuva.ConsumerBff.Entities.Exceptions;
using Kuva.ConsumerBff.Repository.Context;
using Kuva.ConsumerBff.Repository.Repositories;
using Kuva.ConsumerBff.Repository.UnitOfWork;
using Kuva.ConsumerBff.Service.Clients;
using Kuva.ConsumerBff.Service.Middlewares;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace Kuva.ConsumerBff.Tests;

[TestFixture]
public sealed class ConsumerOrderFacadeTests
{
    private readonly Guid _consumerId = Guid.NewGuid();
    private Mock<IStoreServiceClient> _store = null!;
    private Mock<ICatalogPricingServiceClient> _catalog = null!;
    private Mock<IOrderServiceClient> _order = null!;
    private Mock<IMediaServiceClient> _media = null!;
    private Mock<ICurrentConsumerProvider> _current = null!;
    private Mock<IIdempotencyService> _idempotency = null!;
    private Mock<IConsumerBffMetrics> _metrics = null!;
    private ConsumerOrderFacade _facade = null!;

    [SetUp]
    public void SetUp()
    {
        _store = new Mock<IStoreServiceClient>(MockBehavior.Strict);
        _catalog = new Mock<ICatalogPricingServiceClient>(MockBehavior.Strict);
        _order = new Mock<IOrderServiceClient>(MockBehavior.Strict);
        _media = new Mock<IMediaServiceClient>(MockBehavior.Strict);
        _current = new Mock<ICurrentConsumerProvider>(MockBehavior.Strict);
        _idempotency = new Mock<IIdempotencyService>(MockBehavior.Strict);
        _metrics = new Mock<IConsumerBffMetrics>(MockBehavior.Loose);
        _facade = new ConsumerOrderFacade(_store.Object, _catalog.Object, _order.Object, _media.Object, _current.Object, _idempotency.Object, _metrics.Object);
    }

    [Test]
    public async Task CreateOrderAsync_WhenStoreAndSkuAreValid_CreatesOrderAndMetric()
    {
        var storeId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        var request = new CreateConsumerOrderRequest(storeId, [new CreateConsumerOrderItemRequest(skuId, 2)]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _store.Setup(x => x.IsStoreActiveAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _catalog.Setup(x => x.GetSkusAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync([Sku(skuId, true)]);
        _order.Setup(x => x.CreateOrderAsync(It.Is<DownstreamCreateOrderRequest>(r => r.ConsumerId == _consumerId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Order(storeId, _consumerId, skuId));

        var response = await _facade.CreateOrderAsync(request, null, CancellationToken.None);

        Assert.That(response.OrderId, Is.Not.EqualTo(Guid.Empty));
        _metrics.Verify(x => x.OrderCreated(storeId), Times.Once);
    }

    [Test]
    public void CreateOrderAsync_WhenItemListIsEmpty_ThrowsValidation()
    {
        var ex = Assert.ThrowsAsync<ValidationException>(() => _facade.CreateOrderAsync(new CreateConsumerOrderRequest(Guid.NewGuid(), []), null, CancellationToken.None));
        Assert.That(ex!.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public void CreateOrderAsync_WhenQuantityIsZero_ThrowsValidation()
    {
        var ex = Assert.ThrowsAsync<ValidationException>(() => _facade.CreateOrderAsync(new CreateConsumerOrderRequest(Guid.NewGuid(), [new CreateConsumerOrderItemRequest(Guid.NewGuid(), 0)]), null, CancellationToken.None));
        Assert.That(ex!.Errors["items"][0], Does.Contain("quantidade"));
    }

    [Test]
    public void CreateOrderAsync_WhenStoreInactive_ThrowsConflict()
    {
        var request = new CreateConsumerOrderRequest(Guid.NewGuid(), [new CreateConsumerOrderItemRequest(Guid.NewGuid(), 1)]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _store.Setup(x => x.IsStoreActiveAsync(request.StoreId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = Assert.ThrowsAsync<BusinessException>(() => _facade.CreateOrderAsync(request, null, CancellationToken.None));

        Assert.That(ex!.Code, Is.EqualTo("STORE_INACTIVE"));
    }

    [Test]
    public void CreateOrderAsync_WhenSkuUnavailable_ThrowsConflict()
    {
        var request = new CreateConsumerOrderRequest(Guid.NewGuid(), [new CreateConsumerOrderItemRequest(Guid.NewGuid(), 1)]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _store.Setup(x => x.IsStoreActiveAsync(request.StoreId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _catalog.Setup(x => x.GetSkusAsync(request.StoreId, It.IsAny<CancellationToken>())).ReturnsAsync([Sku(Guid.NewGuid(), false)]);

        var ex = Assert.ThrowsAsync<BusinessException>(() => _facade.CreateOrderAsync(request, null, CancellationToken.None));

        Assert.That(ex!.Code, Is.EqualTo("SKU_UNAVAILABLE"));
    }

    [Test]
    public async Task CreateOrderAsync_WhenIdempotencyReplayExists_ReturnsStoredResponse()
    {
        var response = new Kuva.ConsumerBff.Entities.Contracts.Responses.ConsumerOrderResponse(Guid.NewGuid(), "Created", Guid.NewGuid(), 10, 1, "BRL", []);
        var request = new CreateConsumerOrderRequest(response.StoreId, [new CreateConsumerOrderItemRequest(Guid.NewGuid(), 1)]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _idempotency.Setup(x => x.TryGetReplayAsync<Kuva.ConsumerBff.Entities.Contracts.Responses.ConsumerOrderResponse>(_consumerId, "key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdempotencyReplay<Kuva.ConsumerBff.Entities.Contracts.Responses.ConsumerOrderResponse>(response));

        var result = await _facade.CreateOrderAsync(request, "key", CancellationToken.None);

        Assert.That(result.OrderId, Is.EqualTo(response.OrderId));
        _metrics.Verify(x => x.IdempotencyReplay(), Times.Once);
    }

    [Test]
    public void CreateOrderAsync_WhenIdempotencyKeyConflicts_ThrowsConflict()
    {
        var request = new CreateConsumerOrderRequest(Guid.NewGuid(), [new CreateConsumerOrderItemRequest(Guid.NewGuid(), 1)]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _idempotency.Setup(x => x.TryGetReplayAsync<Kuva.ConsumerBff.Entities.Contracts.Responses.ConsumerOrderResponse>(_consumerId, "key", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new BusinessException("IDEMPOTENCY_KEY_CONFLICT", "conflict"));

        var ex = Assert.ThrowsAsync<BusinessException>(() => _facade.CreateOrderAsync(request, "key", CancellationToken.None));

        Assert.That(ex!.Code, Is.EqualTo("IDEMPOTENCY_KEY_CONFLICT"));
    }

    [Test]
    public async Task CreateUploadSessionAsync_WhenOwnOrder_CreatesSession()
    {
        var orderId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var request = new CreateUploadSessionRequest([new UploadPhotoRequest("local", "a.jpg", "image/jpeg", 100, "sha256:abc")]);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Order(storeId, _consumerId, Guid.NewGuid(), orderId));
        _media.Setup(x => x.CreateUploadSessionAsync(orderId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownstreamUploadSession(orderId, DateTimeOffset.UtcNow.AddMinutes(10), [new DownstreamUploadTarget(Guid.NewGuid(), "local", "https://sas", "PUT", new Dictionary<string, string>())]));

        var response = await _facade.CreateUploadSessionAsync(orderId, request, CancellationToken.None);

        Assert.That(response.Uploads, Has.Count.EqualTo(1));
        _metrics.Verify(x => x.UploadSessionCreated(storeId), Times.Once);
    }

    [Test]
    public void CreateUploadSessionAsync_WhenOrderBelongsToAnotherConsumer_ThrowsForbidden()
    {
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Order(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()));

        Assert.ThrowsAsync<ForbiddenResourceException>(() => _facade.CreateUploadSessionAsync(Guid.NewGuid(), new CreateUploadSessionRequest([new UploadPhotoRequest("local", "a.jpg", "image/jpeg", 100, "sha256:abc")]), CancellationToken.None));
    }

    [Test]
    public void CreateUploadSessionAsync_WhenInvalidPhoto_ThrowsValidation()
    {
        var ex = Assert.ThrowsAsync<ValidationException>(() => _facade.CreateUploadSessionAsync(Guid.NewGuid(), new CreateUploadSessionRequest([new UploadPhotoRequest("local", "a.txt", "text/plain", 100, "sha256:abc")]), CancellationToken.None));
        Assert.That(ex!.Errors.ContainsKey("contentType"), Is.True);
    }

    [Test]
    public async Task ConfirmOrderAsync_WhenPhotosValid_ConfirmsAndTracksMetric()
    {
        var orderId = Guid.NewGuid();
        var storeId = Guid.NewGuid();
        var request = new ConfirmConsumerOrderRequest(true, true);
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Order(storeId, _consumerId, Guid.NewGuid(), orderId));
        _media.Setup(x => x.HasValidUploadedPhotosAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _order.Setup(x => x.ConfirmAsync(orderId, request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownstreamConfirmOrder(orderId, "ReceivedByStore", "Pedido enviado para a loja.", new DownstreamPickup("Loja", "A7K9", "Retire no balcão.")));

        var response = await _facade.ConfirmOrderAsync(orderId, request, null, CancellationToken.None);

        Assert.That(response.Status, Is.EqualTo("ReceivedByStore"));
        _metrics.Verify(x => x.OrderConfirmed(storeId), Times.Once);
    }

    [Test]
    public void ConfirmOrderAsync_WhenNoValidPhotos_ThrowsAndTracksFailure()
    {
        var orderId = Guid.NewGuid();
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Order(Guid.NewGuid(), _consumerId, Guid.NewGuid(), orderId));
        _media.Setup(x => x.HasValidUploadedPhotosAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var ex = Assert.ThrowsAsync<BusinessException>(() => _facade.ConfirmOrderAsync(orderId, new ConfirmConsumerOrderRequest(true, true), null, CancellationToken.None));

        Assert.That(ex!.Code, Is.EqualTo("NO_VALID_PHOTOS"));
        _metrics.Verify(x => x.OrderConfirmFailed(), Times.Once);
    }

    [Test]
    public void ConfirmOrderAsync_WhenConsentMissing_ThrowsValidation()
    {
        var ex = Assert.ThrowsAsync<ValidationException>(() => _facade.ConfirmOrderAsync(Guid.NewGuid(), new ConfirmConsumerOrderRequest(true, false), null, CancellationToken.None));
        Assert.That(ex!.Errors.ContainsKey("acceptedPhotoProcessingConsent"), Is.True);
        _metrics.Verify(x => x.OrderConfirmFailed(), Times.Once);
    }

    [Test]
    public async Task GetStatusAsync_WhenAnotherConsumer_ThrowsForbidden()
    {
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetStatusAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DownstreamOrderStatus(Guid.NewGuid(), "Created", Guid.NewGuid()));

        Assert.ThrowsAsync<ForbiddenResourceException>(() => _facade.GetStatusAsync(Guid.NewGuid(), CancellationToken.None));
        await Task.CompletedTask;
    }

    [Test]
    public async Task GetOrdersAsync_ReturnsOnlyCurrentConsumerOrders()
    {
        var skuId = Guid.NewGuid();
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrdersAsync(_consumerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Order(Guid.NewGuid(), _consumerId, skuId), Order(Guid.NewGuid(), Guid.NewGuid(), skuId)]);

        var response = await _facade.GetOrdersAsync(CancellationToken.None);

        Assert.That(response, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task GetOrderAsync_WhenOwnOrder_ReturnsMappedOrder()
    {
        var orderId = Guid.NewGuid();
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Order(Guid.NewGuid(), _consumerId, Guid.NewGuid(), orderId));

        var response = await _facade.GetOrderAsync(orderId, CancellationToken.None);

        Assert.That(response.OrderId, Is.EqualTo(orderId));
    }

    [Test]
    public async Task CancelOrderAsync_WhenOwnOrder_ReturnsCancelledStatus()
    {
        var orderId = Guid.NewGuid();
        _current.Setup(x => x.GetConsumerId()).Returns(_consumerId);
        _order.Setup(x => x.GetOrderAsync(orderId, It.IsAny<CancellationToken>())).ReturnsAsync(Order(Guid.NewGuid(), _consumerId, Guid.NewGuid(), orderId));
        _order.Setup(x => x.CancelAsync(orderId, It.IsAny<CancelConsumerOrderRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new DownstreamOrderStatus(orderId, "Cancelled", _consumerId));

        var response = await _facade.CancelOrderAsync(orderId, new CancelConsumerOrderRequest("mudou"), CancellationToken.None);

        Assert.That(response.Status, Is.EqualTo("Cancelled"));
    }

    private static DownstreamSku Sku(Guid skuId, bool available) =>
        new(skuId, "FOTO", "Foto", [], 2.5m, "BRL", available);

    private static DownstreamOrder Order(Guid storeId, Guid consumerId, Guid skuId, Guid? orderId = null) =>
        new(orderId ?? Guid.NewGuid(), consumerId, "Created", storeId, 5, 2, "BRL", [new DownstreamOrderItem(skuId, "FOTO", "Foto", 2.5m, 2, 5)]);
}

[TestFixture]
public sealed class CatalogAndStoreFacadeTests
{
    [Test]
    public async Task StoreFacade_FiltersOnlyActiveStores()
    {
        var storeClient = new Mock<IStoreServiceClient>();
        storeClient.Setup(x => x.GetActiveStoresAsync(It.IsAny<CancellationToken>())).ReturnsAsync([Store("Active"), Store("Inactive")]);
        var facade = new ConsumerStoreFacade(storeClient.Object);

        var response = await facade.GetStoresAsync(CancellationToken.None);

        Assert.That(response.Items, Has.Count.EqualTo(1));
    }

    [Test]
    public void StoreFacade_WhenStoreInactive_ThrowsConflict()
    {
        var storeClient = new Mock<IStoreServiceClient>();
        storeClient.Setup(x => x.GetStoreAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(Store("Inactive"));
        var facade = new ConsumerStoreFacade(storeClient.Object);

        Assert.ThrowsAsync<BusinessException>(() => facade.GetStoreAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Test]
    public async Task CatalogFacade_FiltersUnavailableSkus()
    {
        var store = new Mock<IStoreServiceClient>();
        var catalog = new Mock<ICatalogPricingServiceClient>();
        var storeId = Guid.NewGuid();
        store.Setup(x => x.IsStoreActiveAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        catalog.Setup(x => x.GetCatalogAsync(storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownstreamCatalog(storeId, [new DownstreamProduct(Guid.NewGuid(), "Produto", "Desc", [new DownstreamSku(Guid.NewGuid(), "A", "Ativo", [], 1, "BRL", true), new DownstreamSku(Guid.NewGuid(), "I", "Inativo", [], 1, "BRL", false)])]));
        var facade = new ConsumerCatalogFacade(store.Object, catalog.Object);

        var response = await facade.GetCatalogAsync(storeId, CancellationToken.None);

        Assert.That(response.Products.Single().Skus, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CatalogFacade_GetSkus_ReturnsOnlyAvailable()
    {
        var store = new Mock<IStoreServiceClient>();
        var catalog = new Mock<ICatalogPricingServiceClient>();
        var storeId = Guid.NewGuid();
        store.Setup(x => x.IsStoreActiveAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        catalog.Setup(x => x.GetSkusAsync(storeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new DownstreamSku(Guid.NewGuid(), "A", "Ativo", [], 1, "BRL", true), new DownstreamSku(Guid.NewGuid(), "I", "Inativo", [], 1, "BRL", false)]);
        var facade = new ConsumerCatalogFacade(store.Object, catalog.Object);

        var response = await facade.GetSkusAsync(storeId, CancellationToken.None);

        Assert.That(response, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task CatalogFacade_GetSku_WhenAvailable_ReturnsSku()
    {
        var store = new Mock<IStoreServiceClient>();
        var catalog = new Mock<ICatalogPricingServiceClient>();
        var storeId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        store.Setup(x => x.IsStoreActiveAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        catalog.Setup(x => x.GetSkuAsync(storeId, skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownstreamSku(skuId, "A", "Ativo", [], 1, "BRL", true));
        var facade = new ConsumerCatalogFacade(store.Object, catalog.Object);

        var response = await facade.GetSkuAsync(storeId, skuId, CancellationToken.None);

        Assert.That(response.SkuId, Is.EqualTo(skuId));
    }

    [Test]
    public void CatalogFacade_GetSku_WhenUnavailable_ThrowsConflict()
    {
        var store = new Mock<IStoreServiceClient>();
        var catalog = new Mock<ICatalogPricingServiceClient>();
        var storeId = Guid.NewGuid();
        var skuId = Guid.NewGuid();
        store.Setup(x => x.IsStoreActiveAsync(storeId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        catalog.Setup(x => x.GetSkuAsync(storeId, skuId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DownstreamSku(skuId, "I", "Inativo", [], 1, "BRL", false));
        var facade = new ConsumerCatalogFacade(store.Object, catalog.Object);

        Assert.ThrowsAsync<BusinessException>(() => facade.GetSkuAsync(storeId, skuId, CancellationToken.None));
    }

    [Test]
    public void CatalogFacade_WhenStoreInactive_ThrowsConflict()
    {
        var store = new Mock<IStoreServiceClient>();
        store.Setup(x => x.IsStoreActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var facade = new ConsumerCatalogFacade(store.Object, new Mock<ICatalogPricingServiceClient>().Object);

        Assert.ThrowsAsync<BusinessException>(() => facade.GetCatalogAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private static DownstreamStore Store(string status) =>
        new(Guid.NewGuid(), "Loja", status, new DownstreamAddress("Rua", "1", "Cidade", "SP", "00000-000"), "Retire no balcão.", "9h às 18h");
}

[TestFixture]
public sealed class RepositoryAndIdempotencyTests
{
    [Test]
    public async Task IdempotencyService_ReplaysCompletedResponse()
    {
        await using var db = CreateDb();
        var repository = new IdempotencyKeyRepository(db);
        var unit = new UnitOfWork(db);
        var service = new IdempotencyService(repository, unit);
        var consumerId = Guid.NewGuid();
        await service.RegisterProcessingAsync(consumerId, "key", "hash", CancellationToken.None);
        await service.CompleteAsync(consumerId, "key", "Order", Guid.NewGuid(), new { Message = "ok" }, CancellationToken.None);

        var replay = await service.TryGetReplayAsync<Dictionary<string, string>>(consumerId, "key", "hash", CancellationToken.None);

        Assert.That(replay!.Response["Message"], Is.EqualTo("ok"));
    }

    [Test]
    public async Task IdempotencyService_WhenHashDiffers_ThrowsConflict()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(new IdempotencyKeyRepository(db), new UnitOfWork(db));
        var consumerId = Guid.NewGuid();
        await service.RegisterProcessingAsync(consumerId, "key", "hash", CancellationToken.None);

        var ex = Assert.ThrowsAsync<BusinessException>(() => service.TryGetReplayAsync<object>(consumerId, "key", "other", CancellationToken.None));

        Assert.That(ex!.Code, Is.EqualTo("IDEMPOTENCY_KEY_CONFLICT"));
    }

    private static ConsumerBffDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<ConsumerBffDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}

[TestFixture]
public sealed class MiddlewareTests
{
    [Test]
    public async Task CorrelationIdMiddleware_CreatesCorrelationIdWhenMissing()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers[HeaderConstants.CorrelationId].ToString(), Is.Not.Empty);
    }

    [Test]
    public async Task CorrelationIdMiddleware_PreservesReceivedCorrelationId()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[HeaderConstants.CorrelationId] = "corr-1";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.Headers[HeaderConstants.CorrelationId].ToString(), Is.EqualTo("corr-1"));
    }

    [Test]
    public async Task ExceptionHandlingMiddleware_ConvertsValidationExceptionToProblemDetails()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionHandlingMiddleware(_ => throw new ValidationException(new Dictionary<string, string[]> { ["items"] = ["erro"] }));

        await middleware.InvokeAsync(context);

        Assert.That(context.Response.StatusCode, Is.EqualTo(400));
    }

    [Test]
    public void SensitiveDataSanitization_SanitizesSasSignature()
    {
        var sanitized = SensitiveDataSanitizationMiddleware.Sanitize("https://blob?sig=secret");
        Assert.That(sanitized, Does.Contain("sig=[redacted]"));
    }
}

[TestFixture]
public sealed class ClientTests
{
    [Test]
    public async Task StoreClient_PropagatesAuthorizationAndCorrelationHeaders()
    {
        var handler = new CapturingHandler(HttpStatusCode.OK, new[] { new DownstreamStore(Guid.NewGuid(), "Loja", "Active", new DownstreamAddress("Rua", "1", "Cidade", "SP", "00000-000"), "Retire", "9h") });
        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer token";
        context.Items[HeaderConstants.CorrelationId] = "corr";
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypesConstants.Subject, Guid.NewGuid().ToString())]));
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(x => x.HttpContext).Returns(context);
        var client = new StoreServiceClient(new HttpClient(handler) { BaseAddress = new Uri("http://store") }, accessor.Object, new Mock<IConsumerBffMetrics>().Object);

        await client.GetActiveStoresAsync(CancellationToken.None);

        Assert.That(handler.Request!.Headers.Authorization!.Scheme, Is.EqualTo("Bearer"));
        Assert.That(handler.Request.Headers.GetValues(HeaderConstants.CorrelationId).Single(), Is.EqualTo("corr"));
    }

    [Test]
    public void StoreClient_MapsNotFoundToNotFoundResourceException()
    {
        var client = new StoreServiceClient(new HttpClient(new CapturingHandler(HttpStatusCode.NotFound, new { })) { BaseAddress = new Uri("http://store") }, new Mock<IHttpContextAccessor>().Object, new Mock<IConsumerBffMetrics>().Object);
        Assert.ThrowsAsync<NotFoundResourceException>(() => client.GetStoreAsync(Guid.NewGuid(), CancellationToken.None));
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly object _payload;
        public HttpRequestMessage? Request { get; private set; }

        public CapturingHandler(HttpStatusCode statusCode, object payload)
        {
            _statusCode = statusCode;
            _payload = payload;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(_statusCode)
            {
                Content = JsonContent.Create(_payload)
            });
        }
    }
}
