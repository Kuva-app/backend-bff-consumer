using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuva.ConsumerBff.Repository.Entities
{

public sealed class IdempotencyKeyEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Key { get; set; } = string.Empty;
    public Guid ConsumerId { get; set; }
    public string RequestHash { get; set; } = string.Empty;
    public string? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? ResponsePayloadJson { get; set; }
    public string Status { get; set; } = "Processing";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ApiRequestAuditEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ConsumerId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public int ElapsedMs { get; set; }
    public string? SanitizedError { get; set; }
    public string? ClientAppVersion { get; set; }
    public string? DevicePlatform { get; set; }
    public string? IpHash { get; set; }
    public string? UserAgentHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ExternalServiceCallEntity> ExternalServiceCalls { get; set; } = [];
}

public sealed class ExternalServiceCallEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? ApiRequestAuditId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string UrlTemplate { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public int ElapsedMs { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? SanitizedError { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ConsumerOrderDraftEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ConsumerId { get; set; }
    public Guid StoreId { get; set; }
    public string ItemsJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
}

namespace Kuva.ConsumerBff.Repository.Context
{

using Kuva.ConsumerBff.Repository.Entities;

public sealed class ConsumerBffDbContext : DbContext
{
    public ConsumerBffDbContext(DbContextOptions<ConsumerBffDbContext> options)
        : base(options)
    {
    }

    public DbSet<IdempotencyKeyEntity> IdempotencyKeys => Set<IdempotencyKeyEntity>();
    public DbSet<ApiRequestAuditEntity> ApiRequestAudits => Set<ApiRequestAuditEntity>();
    public DbSet<ExternalServiceCallEntity> ExternalServiceCalls => Set<ExternalServiceCallEntity>();
    public DbSet<ConsumerOrderDraftEntity> ConsumerOrderDrafts => Set<ConsumerOrderDraftEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("consumer_bff");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsumerBffDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
}

namespace Kuva.ConsumerBff.Repository.Configurations
{

using Kuva.ConsumerBff.Repository.Entities;

public sealed class IdempotencyKeyConfiguration : IEntityTypeConfiguration<IdempotencyKeyEntity>
{
    public void Configure(EntityTypeBuilder<IdempotencyKeyEntity> builder)
    {
        builder.ToTable("idempotency_keys");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Key).HasMaxLength(120).IsRequired();
        builder.Property(x => x.RequestHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.ResourceType).HasMaxLength(80);
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.Key, x.ConsumerId }).IsUnique().HasDatabaseName("UX_idempotency_keys_key_consumer_id");
        builder.HasIndex(x => x.ExpiresAt).HasDatabaseName("IX_idempotency_keys_expires_at");
    }
}

public sealed class ApiRequestAuditConfiguration : IEntityTypeConfiguration<ApiRequestAuditEntity>
{
    public void Configure(EntityTypeBuilder<ApiRequestAuditEntity> builder)
    {
        builder.ToTable("api_request_audits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.CorrelationId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(12).IsRequired();
        builder.Property(x => x.Path).HasMaxLength(300).IsRequired();
        builder.Property(x => x.SanitizedError).HasMaxLength(1000);
        builder.Property(x => x.ClientAppVersion).HasMaxLength(40);
        builder.Property(x => x.DevicePlatform).HasMaxLength(40);
        builder.Property(x => x.IpHash).HasMaxLength(128);
        builder.Property(x => x.UserAgentHash).HasMaxLength(128);
        builder.HasIndex(x => new { x.ConsumerId, x.CreatedAt }).HasDatabaseName("IX_api_request_audits_consumer_id_created_at");
        builder.HasIndex(x => x.CorrelationId).HasDatabaseName("IX_api_request_audits_correlation_id");
        builder.HasIndex(x => new { x.StatusCode, x.CreatedAt }).HasDatabaseName("IX_api_request_audits_status_code_created_at");
    }
}

public sealed class ExternalServiceCallConfiguration : IEntityTypeConfiguration<ExternalServiceCallEntity>
{
    public void Configure(EntityTypeBuilder<ExternalServiceCallEntity> builder)
    {
        builder.ToTable("external_service_calls");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ServiceName).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Operation).HasMaxLength(120).IsRequired();
        builder.Property(x => x.Method).HasMaxLength(12).IsRequired();
        builder.Property(x => x.UrlTemplate).HasMaxLength(300).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(80).IsRequired();
        builder.Property(x => x.SanitizedError).HasMaxLength(1000);
    }
}

public sealed class ConsumerOrderDraftConfiguration : IEntityTypeConfiguration<ConsumerOrderDraftEntity>
{
    public void Configure(EntityTypeBuilder<ConsumerOrderDraftEntity> builder)
    {
        builder.ToTable("consumer_order_drafts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ItemsJson).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(40).IsRequired();
        builder.HasIndex(x => new { x.ConsumerId, x.ExpiresAt }).HasDatabaseName("IX_consumer_order_drafts_consumer_id_expires_at");
    }
}
}

namespace Kuva.ConsumerBff.Repository.Interfaces
{

using Kuva.ConsumerBff.Repository.Entities;

public interface IIdempotencyKeyRepository
{
    Task<IdempotencyKeyEntity?> GetAsync(Guid consumerId, string key, CancellationToken cancellationToken);
    Task AddAsync(Guid consumerId, string key, string requestHash, DateTimeOffset expiresAt, CancellationToken cancellationToken);
    Task CompleteAsync(Guid consumerId, string key, string resourceType, Guid resourceId, string responsePayloadJson, CancellationToken cancellationToken);
}

public interface IApiRequestAuditRepository
{
    Task AddAsync(ApiRequestAuditEntity audit, CancellationToken cancellationToken);
}

public interface IConsumerOrderDraftRepository
{
    Task AddAsync(ConsumerOrderDraftEntity draft, CancellationToken cancellationToken);
}

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
}

namespace Kuva.ConsumerBff.Repository.Repositories
{

using Kuva.ConsumerBff.Repository.Context;
using Kuva.ConsumerBff.Repository.Entities;
using Kuva.ConsumerBff.Repository.Interfaces;

public sealed class IdempotencyKeyRepository : IIdempotencyKeyRepository
{
    private readonly ConsumerBffDbContext _dbContext;

    public IdempotencyKeyRepository(ConsumerBffDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IdempotencyKeyEntity?> GetAsync(Guid consumerId, string key, CancellationToken cancellationToken) =>
        _dbContext.IdempotencyKeys.FirstOrDefaultAsync(x => x.ConsumerId == consumerId && x.Key == key && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

    public async Task AddAsync(Guid consumerId, string key, string requestHash, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        await _dbContext.IdempotencyKeys.AddAsync(new IdempotencyKeyEntity
        {
            ConsumerId = consumerId,
            Key = key,
            RequestHash = requestHash,
            ExpiresAt = expiresAt
        }, cancellationToken);
    }

    public async Task CompleteAsync(Guid consumerId, string key, string resourceType, Guid resourceId, string responsePayloadJson, CancellationToken cancellationToken)
    {
        var entity = await GetAsync(consumerId, key, cancellationToken);
        if (entity is null)
        {
            return;
        }

        entity.Status = "Completed";
        entity.ResourceType = resourceType;
        entity.ResourceId = resourceId;
        entity.ResponsePayloadJson = responsePayloadJson;
        entity.UpdatedAt = DateTimeOffset.UtcNow;
    }
}

public sealed class ApiRequestAuditRepository : IApiRequestAuditRepository
{
    private readonly ConsumerBffDbContext _dbContext;

    public ApiRequestAuditRepository(ConsumerBffDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ApiRequestAuditEntity audit, CancellationToken cancellationToken) =>
        await _dbContext.ApiRequestAudits.AddAsync(audit, cancellationToken);
}

public sealed class ConsumerOrderDraftRepository : IConsumerOrderDraftRepository
{
    private readonly ConsumerBffDbContext _dbContext;

    public ConsumerOrderDraftRepository(ConsumerBffDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(ConsumerOrderDraftEntity draft, CancellationToken cancellationToken) =>
        await _dbContext.ConsumerOrderDrafts.AddAsync(draft, cancellationToken);
}
}

namespace Kuva.ConsumerBff.Repository.UnitOfWork
{

using Kuva.ConsumerBff.Repository.Context;
using Kuva.ConsumerBff.Repository.Interfaces;

public sealed class UnitOfWork : IUnitOfWork
{
    private readonly ConsumerBffDbContext _dbContext;

    public UnitOfWork(ConsumerBffDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        _dbContext.SaveChangesAsync(cancellationToken);
}
}
