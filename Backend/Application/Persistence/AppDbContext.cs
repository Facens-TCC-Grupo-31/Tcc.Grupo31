using Application.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Application.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Sensor> Sensors => Set<Sensor>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<GraphNode> GraphNodes => Set<GraphNode>();
    public DbSet<GraphEdge> GraphEdges => Set<GraphEdge>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Sensor>(e =>
        {
            e.HasKey(s => s.Id);
            
            e.Property(s => s.Id).UseIdentityAlwaysColumn();
            
            e.Property(s => s.CreatedAt).HasDefaultValueSql("now()");
        });

        modelBuilder.Entity<SensorReading>(e =>
        {
            e.HasKey(r => r.Id);

            e.Property(r => r.Id).UseIdentityAlwaysColumn();
            
            e.HasOne(r => r.Sensor)
             .WithMany(s => s.Readings)
             .HasForeignKey(r => r.SensorId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(r => new { r.SensorId, r.Timestamp });
        });

        modelBuilder.Entity<GraphNode>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Id).UseIdentityAlwaysColumn();
        });

        modelBuilder.Entity<GraphEdge>(e =>
        {
            e.HasKey(edge => edge.Id);
            
            e.Property(edge => edge.Id).UseIdentityAlwaysColumn();

            e.HasIndex(edge => edge.FromNodeId);
            
            e.HasIndex(edge => edge.ToNodeId);
            
            e.HasIndex(edge => new { edge.FromNodeId, edge.ToNodeId }).IsUnique();
        });
    }
}
