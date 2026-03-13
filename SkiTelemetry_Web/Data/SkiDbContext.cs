using Microsoft.EntityFrameworkCore;
using SkiTelemetry_Core;

namespace SkiTelemetry_Web.Data;

public class SkiDbContext : DbContext
{
    public DbSet<SkiRun> Runs { get; set; }
    public DbSet<User> Users { get; set; }

    public SkiDbContext(DbContextOptions<SkiDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // 1. Chiave composta per la relazione N:N
        modelBuilder.Entity<CoachAthleteRelation>()
            .HasKey(ca => new { ca.CoachId, ca.AthleteId });

        // 2. Vincolo di unicità sulla Email
        modelBuilder.Entity<User>()
            .HasIndex(u => u.Email)
            .IsUnique();

        // 3. Date gestite dal Database (SQL Server/SQLite)
        modelBuilder.Entity<User>()
            .Property(u => u.CreatedAt)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");

        // Configurazione Relazione Run -> Atleta
        modelBuilder.Entity<SkiRun>()
            .HasOne(r => r.Athlete)          // Una Run ha un Atleta
            .WithMany(u => u.Runs)                      // Un Atleta può avere molte Run (lasciamo vuoto se non aggiungiamo List<SkiRun> in User)
            .HasForeignKey(r => r.AthleteId) // La chiave di collegamento è AthleteId
            .OnDelete(DeleteBehavior.Restrict); // Impedisce di cancellare un Atleta se ha delle Run associate

        modelBuilder.Entity<SkiRun>()
            .Property(r => r.UploadDate)
            .HasDefaultValueSql("CURRENT_TIMESTAMP");
    }
}