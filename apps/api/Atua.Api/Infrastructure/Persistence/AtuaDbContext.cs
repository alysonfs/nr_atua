using Atua.Api.Domain.Billing;
using Atua.Api.Domain.Identity;
using Atua.Api.Domain.Integrations;
using Atua.Api.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atua.Api.Infrastructure.Persistence;

public sealed class AtuaDbContext(DbContextOptions<AtuaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<EmailConfirmation> EmailConfirmations => Set<EmailConfirmation>();

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<TrialSubscription> TrialSubscriptions => Set<TrialSubscription>();

    public DbSet<IntegrationProvider> IntegrationProviders => Set<IntegrationProvider>();

    public DbSet<Integration> Integrations => Set<Integration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUser(modelBuilder.Entity<User>());
        ConfigureEmailConfirmation(modelBuilder.Entity<EmailConfirmation>());
        ConfigureTenant(modelBuilder.Entity<Tenant>());
        ConfigureTenantMembership(modelBuilder.Entity<TenantMembership>());
        ConfigureTrialSubscription(modelBuilder.Entity<TrialSubscription>());
        ConfigureIntegrationProvider(modelBuilder.Entity<IntegrationProvider>());
        ConfigureIntegration(modelBuilder.Entity<Integration>());
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
}