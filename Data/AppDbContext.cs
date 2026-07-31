// Data/AppDbContext.cs

using Microsoft.EntityFrameworkCore;
using PayrollSystem.API.Models;

namespace PayrollSystem.API.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Device> Devices { get; set; }
    public DbSet<DeviceCode> DeviceCodes { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<SalaryPayment> SalaryPayments { get; set; }

    // ❌ DbSet<BudgetApproval> BudgetApprovals { get; set; } removed

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Device configuration
        modelBuilder.Entity<Device>()
            .HasIndex(d => d.AndroidId)
            .IsUnique();

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.InstallationId)
            .IsUnique();

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.DeviceToken)
            .IsUnique();

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.SecretKey)
            .IsUnique()
            .HasDatabaseName("IX_Devices_SecretKey");

        modelBuilder.Entity<Device>()
            .HasIndex(d => d.PublicKey)
            .IsUnique()
            .HasDatabaseName("IX_Devices_PublicKey");

        // User configuration
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Username)
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // DeviceCode configuration
        modelBuilder.Entity<DeviceCode>()
            .HasIndex(dc => dc.Code)
            .IsUnique();

        // Relationships
        modelBuilder.Entity<Device>()
            .HasOne(d => d.User)
            .WithMany(u => u.Devices)
            .HasForeignKey(d => d.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DeviceCode>()
            .HasOne(dc => dc.Device)
            .WithMany()
            .HasForeignKey(dc => dc.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

    
        modelBuilder.Entity<SalaryPayment>()
            .HasOne(sp => sp.Employee)
            .WithMany()
            .HasForeignKey(sp => sp.EmployeeId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}