using Microsoft.EntityFrameworkCore;
using WebApplication1.Data.Entities;

namespace WebApplication1.Data;

public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : DbContext(options)
{
    public DbSet<TrackedPool> TrackedPools => Set<TrackedPool>();

    public DbSet<TelegramUser> TelegramUsers => Set<TelegramUser>();

    public DbSet<PriceAlertSubscription> PriceAlertSubscriptions => Set<PriceAlertSubscription>();

    public DbSet<TelegramChatSession> TelegramChatSessions => Set<TelegramChatSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TelegramUser>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TelegramUserId).IsUnique();
            entity.Property(x => x.Username).HasMaxLength(128);
            entity.Property(x => x.FirstName).HasMaxLength(256);
        });

        modelBuilder.Entity<TrackedPool>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.PoolAddress).IsUnique();
            entity.Property(x => x.PoolAddress).HasMaxLength(128);
            entity.Property(x => x.Token0Address).HasMaxLength(128);
            entity.Property(x => x.Token1Address).HasMaxLength(128);
            entity.Property(x => x.Token0Symbol).HasMaxLength(32);
            entity.Property(x => x.Token1Symbol).HasMaxLength(32);
            entity.Property(x => x.LastKnownPrice).HasPrecision(38, 18);
            entity.Property(x => x.LastKnownInversePrice).HasPrecision(38, 18);
        });

        modelBuilder.Entity<PriceAlertSubscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.TelegramUserId, x.TrackedPoolId }).IsUnique();
            entity.Property(x => x.ThresholdPercent).HasPrecision(18, 8);
            entity.Property(x => x.BasePrice).HasPrecision(38, 18);

            entity.HasOne(x => x.TelegramUser)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.TelegramUserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.TrackedPool)
                .WithMany(x => x.Subscriptions)
                .HasForeignKey(x => x.TrackedPoolId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TelegramChatSession>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TelegramUserId).IsUnique();
            entity.Property(x => x.State).HasConversion<string>().HasMaxLength(64);
            entity.Property(x => x.PendingPoolAddress).HasMaxLength(128);

            entity.HasOne(x => x.TelegramUser)
                .WithOne(x => x.ChatSession)
                .HasForeignKey<TelegramChatSession>(x => x.TelegramUserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
