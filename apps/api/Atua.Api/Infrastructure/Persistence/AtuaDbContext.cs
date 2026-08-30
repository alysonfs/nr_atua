using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Integrations.CollectorControl;
using Atua.Api.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atua.Api.Infrastructure.Persistence;

public sealed class AtuaDbContext(DbContextOptions<AtuaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<EmailConfirmation> EmailConfirmations => Set<EmailConfirmation>();

    public DbSet<AuthSession> AuthSessions => Set<AuthSession>();
    public DbSet<AuthRefreshToken> AuthRefreshTokens => Set<AuthRefreshToken>();
    public DbSet<ServiceCredential> ServiceCredentials => Set<ServiceCredential>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<TrialSubscription> TrialSubscriptions => Set<TrialSubscription>();

    public DbSet<IntegrationProvider> IntegrationProviders => Set<IntegrationProvider>();

    public DbSet<Integration> Integrations => Set<Integration>();

    public DbSet<IServiceCredential> IServiceCredentials => Set<IServiceCredential>();

    public DbSet<CollectorActivation> CollectorActivations => Set<CollectorActivation>();

    public DbSet<ImmediateCollectionCommand> ImmediateCollectionCommands =>
        Set<ImmediateCollectionCommand>();

    public DbSet<CollectorControlIdempotency> CollectorControlIdempotencies =>
        Set<CollectorControlIdempotency>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder.Entity<User>());
        ConfigureEmailConfirmation(modelBuilder.Entity<EmailConfirmation>());
        ConfigureAuthSession(modelBuilder.Entity<AuthSession>());
        ConfigureAuthRefreshToken(modelBuilder.Entity<AuthRefreshToken>());
        ConfigureServiceCredential(modelBuilder.Entity<ServiceCredential>());
        ConfigureTenant(modelBuilder.Entity<Tenant>());
        ConfigureTenantMembership(modelBuilder.Entity<TenantMembership>());
        ConfigureTrialSubscription(modelBuilder.Entity<TrialSubscription>());
        ConfigureIntegrationProvider(modelBuilder.Entity<IntegrationProvider>());
        ConfigureIntegration(modelBuilder.Entity<Integration>());
        ConfigureIServiceCredential(modelBuilder.Entity<IServiceCredential>());
        ConfigureCollectorActivation(modelBuilder.Entity<CollectorActivation>());
        ConfigureImmediateCollectionCommand(modelBuilder.Entity<ImmediateCollectionCommand>());
        ConfigureCollectorControlIdempotency(modelBuilder.Entity<CollectorControlIdempotency>());
    }

    private static void ConfigureAuthSession(EntityTypeBuilder<AuthSession> builder)
    {
        builder.ToTable("auth_sessions");
        builder.HasKey(session => session.Id);
        builder.Property(session => session.Id).ValueGeneratedNever();
        builder.Property(session => session.TimeZoneOverrideId).HasMaxLength(64);
        builder.HasIndex(session => session.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(session => session.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureAuthRefreshToken(EntityTypeBuilder<AuthRefreshToken> builder)
    {
        builder.ToTable("auth_refresh_tokens");
        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();
        builder.Property(token => token.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => token.FamilyId);
        builder.HasOne<AuthSession>().WithMany().HasForeignKey(token => token.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureServiceCredential(EntityTypeBuilder<ServiceCredential> builder)
    {
        builder.ToTable("service_credentials");
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).ValueGeneratedNever();
        builder.Property(credential => credential.TokenHash).HasMaxLength(64).IsRequired();
        builder.Property(credential => credential.Scope).HasMaxLength(128).IsRequired();
        builder.HasIndex(credential => credential.TokenHash).IsUnique();
        builder.HasIndex(credential => credential.TenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(credential => credential.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Integration>().WithMany().HasForeignKey(credential => credential.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IntegrationProvider>().WithMany().HasForeignKey(credential => credential.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureUser(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedNever();
        builder.Property(user => user.Name).HasMaxLength(200);
        builder.Property(user => user.Email).HasMaxLength(320).IsRequired();
        builder.Property(user => user.PasswordHash).HasMaxLength(512).IsRequired();
        builder.Property(user => user.GlobalRole).HasConversion<string>().HasMaxLength(16)
            .IsRequired();
        builder.HasIndex(user => user.Email).IsUnique();
    }

    private static void ConfigureEmailConfirmation(EntityTypeBuilder<EmailConfirmation> builder)
    {
        builder.ToTable("email_confirmations");
        builder.HasKey(confirmation => confirmation.Id);
        builder.Property(confirmation => confirmation.Id).ValueGeneratedNever();
        builder.Property(confirmation => confirmation.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(confirmation => confirmation.ExpiresAt).IsRequired();
        builder.HasIndex(confirmation => confirmation.UserId);
        builder.HasOne<User>().WithMany().HasForeignKey(confirmation => confirmation.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureTenant(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Id).ValueGeneratedNever();
        builder.Property(tenant => tenant.Name).HasMaxLength(200).IsRequired();
        builder.Property(tenant => tenant.Cnpj).HasMaxLength(14).IsRequired();
        builder.Property(tenant => tenant.TimeZoneId).HasMaxLength(64).IsRequired();
        builder.HasIndex(tenant => tenant.Cnpj).IsUnique();
    }

    private static void ConfigureTenantMembership(EntityTypeBuilder<TenantMembership> builder)
    {
        builder.ToTable("tenant_memberships");
        builder.HasKey(membership => new { membership.TenantId, membership.UserId });
        builder.Property(membership => membership.Role).HasConversion<string>().HasMaxLength(16)
            .IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(membership => membership.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(membership => membership.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureTrialSubscription(EntityTypeBuilder<TrialSubscription> builder)
    {
        builder.ToTable("trial_subscriptions");
        builder.HasKey(subscription => subscription.Id);
        builder.Property(subscription => subscription.Id).ValueGeneratedNever();
        builder.Property(subscription => subscription.StartsAt).IsRequired();
        builder.Property(subscription => subscription.ExpiresAt).IsRequired();
        builder.HasOne<User>().WithMany().HasForeignKey(subscription => subscription.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(subscription => subscription.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(subscription => subscription.UserId).IsUnique();
    }

    private static void ConfigureIntegrationProvider(EntityTypeBuilder<IntegrationProvider> builder)
    {
        builder.ToTable("integration_providers");
        builder.HasKey(provider => provider.Id);
        builder.Property(provider => provider.Id).ValueGeneratedNever();
        builder.Property(provider => provider.Name).HasMaxLength(100).IsRequired();
        builder.Property(provider => provider.Manufacturer).HasMaxLength(100).IsRequired();
        builder.Property(provider => provider.BaseUri).HasConversion(
            uri => uri.AbsoluteUri,
            value => new Uri(value)).HasMaxLength(2_048).IsRequired();
        builder.HasIndex(provider => provider.Name).IsUnique();

        // Emenda ADR-018 ("Resolução de integrationId"): seed do provedor
        // fixo "iService", referenciado por
        // WellKnownIntegrationProviders.IServiceProviderId. Declarado aqui
        // via HasData para que o EF Core registre o seed no model snapshot
        // e não o considere drift em migrations futuras.
        builder.HasData(new
        {
            Id = WellKnownIntegrationProviders.IServiceProviderId,
            Name = "iService",
            Manufacturer = "iService",
            BaseUri = new Uri("https://iservice.example.com/"),
            IsActive = true
        });
    }

    private static void ConfigureIntegration(EntityTypeBuilder<Integration> builder)
    {
        builder.ToTable("integrations");
        builder.HasKey(integration => integration.Id);
        builder.Property(integration => integration.Id).ValueGeneratedNever();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(integration => integration.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IntegrationProvider>().WithMany()
            .HasForeignKey(integration => integration.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(integration => integration.TenantId);
    }

    private static void ConfigureIServiceCredential(EntityTypeBuilder<IServiceCredential> builder)
    {
        builder.ToTable("iservice_credentials");
        builder.HasKey(credential => credential.Id);
        builder.Property(credential => credential.Id).ValueGeneratedNever();
        builder.Property(credential => credential.UsernameCiphertext).IsRequired();
        builder.Property(credential => credential.PasswordCiphertext).IsRequired();
        builder.Property(credential => credential.DataKeyCiphertext).IsRequired();
        builder.Property(credential => credential.Nonce).IsRequired();
        builder.Property(credential => credential.Tag).IsRequired();
        builder.Property(credential => credential.ValidationStatus).HasConversion<string>()
            .HasMaxLength(16).IsRequired();
        builder.HasIndex(credential => credential.IntegrationId).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(credential => credential.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Integration>().WithMany().HasForeignKey(credential => credential.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureCollectorActivation(EntityTypeBuilder<CollectorActivation> builder)
    {
        builder.ToTable("collector_activations");
        builder.HasKey(activation => activation.Id);
        builder.Property(activation => activation.Id).ValueGeneratedNever();
        builder.Property(activation => activation.Status).HasConversion<string>().HasMaxLength(16)
            .IsRequired();
        builder.Property(activation => activation.DeactivationReason).HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(activation => activation.ConcurrencyToken).IsConcurrencyToken();
        // ADR-020: uma ativacao por integracao.
        builder.HasIndex(activation => activation.IntegrationId).IsUnique();
        builder.HasIndex(activation => activation.TenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(activation => activation.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Integration>().WithMany().HasForeignKey(activation => activation.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    private static void ConfigureImmediateCollectionCommand(
        EntityTypeBuilder<ImmediateCollectionCommand> builder)
    {
        builder.ToTable("immediate_collection_commands");
        builder.HasKey(command => command.Id);
        builder.Property(command => command.Id).ValueGeneratedNever();
        builder.Property(command => command.Status).HasConversion<string>().HasMaxLength(16)
            .IsRequired();
        builder.Property(command => command.CancellationReason).HasConversion<string>()
            .HasMaxLength(32);
        // ADR-021/C3: FailureReason é enum separado do CancellationReason.
        builder.Property(command => command.FailureReason).HasConversion<string>()
            .HasMaxLength(32);
        builder.Property(command => command.ConcurrencyToken).IsConcurrencyToken();
        builder.HasIndex(command => command.TenantId);
        // ADR-020: indice unico parcial garante no maximo um comando Pending
        // por integracao, inclusive sob ativacoes concorrentes (RN-008.4).
        builder.HasIndex(command => command.IntegrationId)
            .IsUnique()
            .HasFilter("\"Status\" = 'Pending'")
            .HasDatabaseName("IX_immediate_collection_commands_IntegrationId_Pending");
        // ADR-021/D2: indice para varredura eficiente pelo ClaimTimeoutJob.
        builder.HasIndex(command => new { command.Status, command.ClaimExpiresAtUtc })
            .HasDatabaseName("IX_immediate_collection_commands_Status_ClaimExpiresAtUtc");
        builder.HasOne<Tenant>().WithMany().HasForeignKey(command => command.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Integration>().WithMany().HasForeignKey(command => command.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<IntegrationProvider>().WithMany().HasForeignKey(command => command.ProviderId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    private static void ConfigureCollectorControlIdempotency(
        EntityTypeBuilder<CollectorControlIdempotency> builder)
    {
        builder.ToTable("collector_control_idempotencies");
        builder.HasKey(record => record.Id);
        builder.Property(record => record.Id).ValueGeneratedNever();
        builder.Property(record => record.Operation).HasConversion<string>().HasMaxLength(16)
            .IsRequired();
        builder.Property(record => record.IdempotencyKey).HasMaxLength(128).IsRequired();
        builder.Property(record => record.RequestHash).HasMaxLength(64).IsRequired();
        builder.Property(record => record.ResponseSnapshot).IsRequired();
        // ADR-020: unicidade (IntegrationId, Operation, IdempotencyKey).
        builder.HasIndex(record => new
        {
            record.IntegrationId, record.Operation, record.IdempotencyKey
        }).IsUnique();
        builder.HasIndex(record => record.TenantId);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(record => record.TenantId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Integration>().WithMany().HasForeignKey(record => record.IntegrationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
