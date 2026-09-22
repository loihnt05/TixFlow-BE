using Microsoft.EntityFrameworkCore;
using TixFlow.Domain.Booking;
using TixFlow.Domain.Common;
using TixFlow.Domain.Events;
using TixFlow.Domain.Identity;
using TixFlow.Domain.Messaging;
using TixFlow.Domain.Orders;
using TixFlow.Domain.Organizers;

namespace TixFlow.Infrastructure.Persistence;

public sealed class TixFlowDbContext(DbContextOptions<TixFlowDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Organizer> Organizers => Set<Organizer>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<Event> Events => Set<Event>();

    public DbSet<EventSession> EventSessions => Set<EventSession>();

    public DbSet<TicketType> TicketTypes => Set<TicketType>();

    public DbSet<Seat> Seats => Set<Seat>();

    public DbSet<TicketInventory> TicketInventories => Set<TicketInventory>();

    public DbSet<Hold> Holds => Set<Hold>();

    public DbSet<HoldItem> HoldItems => Set<HoldItem>();

    public DbSet<SeatAllocation> SeatAllocations => Set<SeatAllocation>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Ticket> Tickets => Set<Ticket>();

    public DbSet<PaymentWebhookEvent> PaymentWebhookEvents => Set<PaymentWebhookEvent>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ConfigureUsers(modelBuilder);
        ConfigureOrganizers(modelBuilder);
        ConfigureEvents(modelBuilder);
        ConfigureBooking(modelBuilder);
        ConfigureOrders(modelBuilder);
        ConfigureOperationalTables(modelBuilder);
    }

    private static void ConfigureUsers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Email).HasMaxLength(320).IsRequired();
            entity.Property(item => item.NormalizedEmail).HasMaxLength(320).IsRequired();
            entity.Property(item => item.DisplayName).HasMaxLength(120).IsRequired();
            entity.Property(item => item.PasswordHash).HasMaxLength(500).IsRequired();
            entity.Property(item => item.Role).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => item.NormalizedEmail).IsUnique();
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.ToTable("refresh_tokens");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TokenHash).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ReplacedByTokenHash).HasMaxLength(128);
            entity.Property(item => item.CreatedByIp).HasMaxLength(64);
            entity.Property(item => item.RevokedByIp).HasMaxLength(64);
            entity.HasIndex(item => item.TokenHash).IsUnique();
            entity.HasIndex(item => new { item.UserId, item.ExpiresAtUtc });
            entity.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Organizer>(entity =>
        {
            entity.ToTable("organizers");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Slug).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(2000).IsRequired();
            entity.Property(item => item.ContactEmail).HasMaxLength(320).IsRequired();
            entity.HasIndex(item => item.Slug).IsUnique();
            entity.HasIndex(item => item.OwnerUserId).IsUnique();
            entity.HasOne(item => item.OwnerUser)
                .WithMany()
                .HasForeignKey(item => item.OwnerUserId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureOrganizers(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Venue>(entity =>
        {
            entity.ToTable("venues");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(180).IsRequired();
            entity.Property(item => item.AddressLine).HasMaxLength(300).IsRequired();
            entity.Property(item => item.Ward).HasMaxLength(120);
            entity.Property(item => item.District).HasMaxLength(120);
            entity.Property(item => item.City).HasMaxLength(120).IsRequired();
            entity.Property(item => item.CountryCode).HasMaxLength(2).IsRequired();
            entity.Property(item => item.TimeZone).HasMaxLength(80).IsRequired();
        });
    }

    private static void ConfigureEvents(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Event>(entity =>
        {
            entity.ToTable("events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(240).IsRequired();
            entity.Property(item => item.Slug).HasMaxLength(240).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(5000).IsRequired();
            entity.Property(item => item.Category).HasMaxLength(100).IsRequired();
            entity.Property(item => item.ImageUrl).HasMaxLength(1000);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => new { item.OrganizerId, item.Slug }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.SaleStartsAtUtc });
            entity.HasOne(item => item.Organizer)
                .WithMany()
                .HasForeignKey(item => item.OrganizerId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Venue)
                .WithMany()
                .HasForeignKey(item => item.VenueId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<EventSession>(entity =>
        {
            entity.ToTable(
                "event_sessions",
                table => table.HasCheckConstraint(
                    "ck_event_sessions_time",
                    "ends_at_utc > starts_at_utc"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(180).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => new { item.EventId, item.StartsAtUtc }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.StartsAtUtc });
            entity.HasOne(item => item.Event)
                .WithMany(item => item.Sessions)
                .HasForeignKey(item => item.EventId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TicketType>(entity =>
        {
            entity.ToTable(
                "ticket_types",
                table =>
                {
                    table.HasCheckConstraint("ck_ticket_types_price", "price >= 0");
                    table.HasCheckConstraint("ck_ticket_types_capacity", "capacity > 0");
                    table.HasCheckConstraint("ck_ticket_types_max_per_order", "max_per_order > 0");
                });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Name).HasMaxLength(160).IsRequired();
            entity.Property(item => item.Code).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Description).HasMaxLength(1000);
            entity.Property(item => item.Price).HasPrecision(14, 2);
            entity.Property(item => item.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(item => item.InventoryMode).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.HasIndex(item => new { item.EventSessionId, item.Code }).IsUnique();
            entity.HasOne(item => item.EventSession)
                .WithMany(item => item.TicketTypes)
                .HasForeignKey(item => item.EventSessionId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Seat>(entity =>
        {
            entity.ToTable("seats");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Section).HasMaxLength(80).IsRequired();
            entity.Property(item => item.RowLabel).HasMaxLength(20).IsRequired();
            entity.Property(item => item.SeatNumber).HasMaxLength(20).IsRequired();
            entity.HasIndex(item => new
            {
                item.EventSessionId,
                item.Section,
                item.RowLabel,
                item.SeatNumber
            }).IsUnique();
            entity.HasIndex(item => item.TicketTypeId);
            entity.HasOne(item => item.EventSession)
                .WithMany(item => item.Seats)
                .HasForeignKey(item => item.EventSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.TicketType)
                .WithMany(item => item.Seats)
                .HasForeignKey(item => item.TicketTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureBooking(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TicketInventory>(entity =>
        {
            entity.ToTable(
                "ticket_inventories",
                table =>
                {
                    table.HasCheckConstraint("ck_ticket_inventories_total", "total_quantity > 0");
                    table.HasCheckConstraint("ck_ticket_inventories_held", "held_quantity >= 0");
                    table.HasCheckConstraint("ck_ticket_inventories_sold", "sold_quantity >= 0");
                    table.HasCheckConstraint(
                        "ck_ticket_inventories_available",
                        "held_quantity + sold_quantity <= total_quantity");
                });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Version).IsConcurrencyToken();
            entity.HasIndex(item => item.TicketTypeId).IsUnique();
            entity.HasOne(item => item.TicketType)
                .WithOne(item => item.Inventory)
                .HasForeignKey<TicketInventory>(item => item.TicketTypeId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Hold>(entity =>
        {
            entity.ToTable("holds");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => new { item.UserId, item.IdempotencyKey }).IsUnique();
            entity.HasIndex(item => new { item.Status, item.ExpiresAtUtc });
            entity.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.EventSession)
                .WithMany()
                .HasForeignKey(item => item.EventSessionId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<HoldItem>(entity =>
        {
            entity.ToTable(
                "hold_items",
                table =>
                {
                    table.HasCheckConstraint("ck_hold_items_quantity", "quantity > 0");
                    table.HasCheckConstraint("ck_hold_items_price", "unit_price >= 0");
                });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.UnitPrice).HasPrecision(14, 2);
            entity.HasIndex(item => item.HoldId);
            entity.HasOne(item => item.Hold)
                .WithMany(item => item.Items)
                .HasForeignKey(item => item.HoldId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.TicketType)
                .WithMany()
                .HasForeignKey(item => item.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<SeatAllocation>(entity =>
        {
            entity.ToTable("seat_allocations");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => item.SeatId)
                .IsUnique()
                .HasFilter("status IN ('Held', 'Sold')");
            entity.HasIndex(item => new { item.Status, item.ExpiresAtUtc });
            entity.HasOne(item => item.Seat)
                .WithMany()
                .HasForeignKey(item => item.SeatId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.HoldItem)
                .WithMany(item => item.SeatAllocations)
                .HasForeignKey(item => item.HoldItemId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasOne(item => item.OrderItem)
                .WithMany(item => item.SeatAllocations)
                .HasForeignKey(item => item.OrderItemId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigureOrders(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable(
                "orders",
                table => table.HasCheckConstraint("ck_orders_total", "total_amount >= 0"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.OrderNumber).HasMaxLength(40).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(item => item.TotalAmount).HasPrecision(14, 2);
            entity.Property(item => item.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.HasIndex(item => item.OrderNumber).IsUnique();
            entity.HasIndex(item => item.HoldId).IsUnique();
            entity.HasIndex(item => new { item.UserId, item.CreatedAtUtc });
            entity.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.EventSession)
                .WithMany()
                .HasForeignKey(item => item.EventSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Hold)
                .WithMany()
                .HasForeignKey(item => item.HoldId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable(
                "order_items",
                table =>
                {
                    table.HasCheckConstraint("ck_order_items_quantity", "quantity > 0");
                    table.HasCheckConstraint("ck_order_items_price", "unit_price >= 0");
                });
            entity.HasKey(item => item.Id);
            entity.Property(item => item.UnitPrice).HasPrecision(14, 2);
            entity.HasIndex(item => item.OrderId);
            entity.HasOne(item => item.Order)
                .WithMany(item => item.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(item => item.TicketType)
                .WithMany()
                .HasForeignKey(item => item.TicketTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable(
                "payments",
                table => table.HasCheckConstraint("ck_payments_amount", "amount >= 0"));
            entity.HasKey(item => item.Id);
            entity.Property(item => item.IdempotencyKey).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Method).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.Property(item => item.Amount).HasPrecision(14, 2);
            entity.Property(item => item.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            entity.Property(item => item.ProviderTransactionId).HasMaxLength(200);
            entity.HasIndex(item => item.IdempotencyKey).IsUnique();
            entity.HasIndex(item => item.ProviderTransactionId)
                .IsUnique()
                .HasFilter("provider_transaction_id IS NOT NULL");
            entity.HasOne(item => item.Order)
                .WithMany(item => item.Payments)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Ticket>(entity =>
        {
            entity.ToTable("tickets");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.TicketCode).HasMaxLength(50).IsRequired();
            entity.Property(item => item.QrTokenHash).HasMaxLength(128).IsRequired();
            entity.Property(item => item.Status).HasConversion<string>().HasMaxLength(24).IsRequired();
            entity.HasIndex(item => item.TicketCode).IsUnique();
            entity.HasIndex(item => item.QrTokenHash).IsUnique();
            entity.HasIndex(item => new { item.EventSessionId, item.Status });
            entity.HasIndex(item => item.SeatId)
                .IsUnique()
                .HasFilter("seat_id IS NOT NULL AND status IN ('Issued', 'Used')");
            entity.HasOne(item => item.OrderItem)
                .WithMany()
                .HasForeignKey(item => item.OrderItemId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.EventSession)
                .WithMany()
                .HasForeignKey(item => item.EventSessionId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(item => item.Seat)
                .WithMany()
                .HasForeignKey(item => item.SeatId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PaymentWebhookEvent>(entity =>
        {
            entity.ToTable("payment_webhook_events");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Provider).HasMaxLength(60).IsRequired();
            entity.Property(item => item.ProviderEventId).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
            entity.Property(item => item.ProcessingError).HasMaxLength(4000);
            entity.HasIndex(item => new { item.Provider, item.ProviderEventId }).IsUnique();
            entity.HasIndex(item => item.ProcessedAtUtc);
        });
    }

    private static void ConfigureOperationalTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.ToTable("idempotency_records");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Scope).HasMaxLength(80).IsRequired();
            entity.Property(item => item.Key).HasMaxLength(120).IsRequired();
            entity.Property(item => item.RequestHash).HasMaxLength(128).IsRequired();
            entity.Property(item => item.ResponseBody).HasColumnType("jsonb");
            entity.HasIndex(item => new { item.UserId, item.Scope, item.Key }).IsUnique();
            entity.HasIndex(item => item.ExpiresAtUtc);
            entity.HasOne(item => item.User)
                .WithMany()
                .HasForeignKey(item => item.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Type).HasMaxLength(200).IsRequired();
            entity.Property(item => item.Payload).HasColumnType("jsonb").IsRequired();
            entity.HasIndex(item => new { item.ProcessedAtUtc, item.NextAttemptAtUtc });
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("audit_logs");
            entity.HasKey(item => item.Id);
            entity.Property(item => item.Action).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EntityType).HasMaxLength(120).IsRequired();
            entity.Property(item => item.EntityId).HasMaxLength(120).IsRequired();
            entity.Property(item => item.Data).HasColumnType("jsonb");
            entity.Property(item => item.IpAddress).HasMaxLength(64);
            entity.HasIndex(item => new { item.EntityType, item.EntityId, item.CreatedAtUtc });
            entity.HasIndex(item => new { item.ActorUserId, item.CreatedAtUtc });
        });
    }
}
