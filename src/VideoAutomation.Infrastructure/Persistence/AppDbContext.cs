using Microsoft.EntityFrameworkCore;
using VideoAutomation.Application.Interfaces;
using VideoAutomation.Domain.Entities;

namespace VideoAutomation.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    private readonly ICurrentUserId _currentUser;

    public AppDbContext(DbContextOptions<AppDbContext> options, ICurrentUserId currentUser) : base(options)
    {
        _currentUser = currentUser;
    }

    // Referenced by the global query filters below; EF re-evaluates it per query, so switching users
    // (or a background job's active user) scopes reads automatically without touching any query site.
    private int? CurrentUserId => _currentUser.Value;

    public DbSet<Trend> Trends => Set<Trend>();
    public DbSet<Video> Videos => Set<Video>();
    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<Scene> Scenes => Set<Scene>();
    public DbSet<Publication> Publications => Set<Publication>();
    public DbSet<Template> Templates => Set<Template>();
    public DbSet<PipelineRun> PipelineRuns => Set<PipelineRun>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<TelegramSession> TelegramSessions => Set<TelegramSession>();
    public DbSet<User> Users => Set<User>();
    public DbSet<PublicationStat> PublicationStats => Set<PublicationStat>();
    public DbSet<ApiUsage> ApiUsages => Set<ApiUsage>();
    public DbSet<ScheduledRun> ScheduledRuns => Set<ScheduledRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Per-user data isolation: every read of these entities is auto-filtered to the current user.
        // (Script/Scene/Publication are reached only through a user-scoped Video, so they don't need their
        // own filter.) Inserts get UserId stamped in ApplyUserAndTimestamps.
        modelBuilder.Entity<Trend>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<Video>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ChatMessage>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<TelegramSession>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<PipelineRun>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<PublicationStat>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ApiUsage>().HasQueryFilter(e => e.UserId == CurrentUserId);
        modelBuilder.Entity<ScheduledRun>().HasQueryFilter(e => e.UserId == CurrentUserId);

        // Trend
        modelBuilder.Entity<Trend>(e =>
        {
            e.HasKey(t => t.Id);
            e.HasIndex(t => t.SourcePlatform);
            e.HasIndex(t => t.FinalViralityScore);
            e.OwnsOne(t => t.ViralityScore, vs =>
            {
                vs.Property(v => v.TrendMomentum).HasColumnName("ViralityTrendMomentum");
                vs.Property(v => v.ViewVelocity).HasColumnName("ViralityViewVelocity");
                vs.Property(v => v.OutlierFactor).HasColumnName("ViralityOutlierFactor");
                vs.Property(v => v.EngagementRate).HasColumnName("ViralityEngagementRate");
                vs.Property(v => v.CrossPlatformCount).HasColumnName("ViralityCrossPlatformCount");
                // Persisted (not just kept in memory) so FinalScore/GetLabel() stay consistent after a DB
                // round-trip — without these, re-loading a Trend would recompute FinalScore using the
                // property defaults instead of the per-platform caps actually used when it was scored,
                // silently disagreeing with the already-persisted FinalViralityScore column.
                vs.Property(v => v.VelocityCapPerHour).HasColumnName("ViralityVelocityCapPerHour");
                vs.Property(v => v.OutlierMultiplierCap).HasColumnName("ViralityOutlierMultiplierCap");
            });
        });

        // Video
        modelBuilder.Entity<Video>(e =>
        {
            e.HasKey(v => v.Id);
            e.HasOne(v => v.Trend).WithMany(t => t.Videos).HasForeignKey(v => v.TrendId);
            e.HasOne(v => v.Script).WithOne(s => s.Video).HasForeignKey<Video>(v => v.ScriptId);
        });

        // Scene
        modelBuilder.Entity<Scene>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Video).WithMany(v => v.Scenes).HasForeignKey(s => s.VideoId);
            e.HasIndex(s => new { s.VideoId, s.OrderIndex });
        });

        // Publication
        modelBuilder.Entity<Publication>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Video).WithMany(v => v.Publications).HasForeignKey(p => p.VideoId);
            e.HasIndex(p => new { p.VideoId, p.Platform }).IsUnique();
        });

        // PipelineRun
        modelBuilder.Entity<PipelineRun>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasOne(p => p.Video).WithMany().HasForeignKey(p => p.VideoId);
        });

        // PublicationStat (time-series snapshots of a Publication's public stats)
        modelBuilder.Entity<PublicationStat>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasOne(s => s.Publication).WithMany().HasForeignKey(s => s.PublicationId);
            e.HasIndex(s => new { s.PublicationId, s.CreatedAt });
        });

        // ApiUsage (per-user/provider/day counter; unique key enables the atomic UPSERT increment)
        modelBuilder.Entity<ApiUsage>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => new { u.UserId, u.Provider, u.Day }).IsUnique();
        });

        // ScheduledRun (content calendar — future full-pipeline runs)
        modelBuilder.Entity<ScheduledRun>(e =>
        {
            e.HasKey(s => s.Id);
            e.HasIndex(s => s.ScheduledAtUtc);
        });

        // Seed default templates
        modelBuilder.Entity<Template>().HasData(
            new Template
            {
                Id = 1, Name = "Belgesel", Description = "Sinematik, ciddi anlatım",
                Type = Domain.Enums.TemplateType.Documentary,
                TTSVoice = "tr-TR-AhmetNeural", TTSRate = 0.95,
                TransitionStyle = "fade", UseABSplit = false, IsDefault = true,
                FontFamily = "Arial", FontColor = "#FFFFFF", FontSize = 48,
                SubtitlePosition = "bottom", BGMVolume = 0.12, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Template
            {
                Id = 2, Name = "Eğitim", Description = "Bilgilendirici, arkadaşça ton",
                Type = Domain.Enums.TemplateType.Education,
                TTSVoice = "tr-TR-EmelNeural", TTSRate = 1.0,
                TransitionStyle = "slide", UseABSplit = false, IsDefault = false,
                FontFamily = "Arial", FontColor = "#FFFFFF", FontSize = 52,
                SubtitlePosition = "bottom", BGMVolume = 0.10, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Template
            {
                Id = 3, Name = "Eğlence", Description = "Enerjik, hızlı kesimler",
                Type = Domain.Enums.TemplateType.Entertainment,
                TTSVoice = "tr-TR-AhmetNeural", TTSRate = 1.1,
                TransitionStyle = "wipe", UseABSplit = false, IsDefault = false,
                FontFamily = "Arial", FontColor = "#FFD700", FontSize = 56,
                SubtitlePosition = "center", BGMVolume = 0.20, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Template
            {
                Id = 4, Name = "Haber", Description = "Profesyonel, resmi ton",
                Type = Domain.Enums.TemplateType.News,
                TTSVoice = "tr-TR-AhmetNeural", TTSRate = 1.0,
                TransitionStyle = "fade", UseABSplit = false, IsDefault = false,
                FontFamily = "Arial", FontColor = "#FFFFFF", FontSize = 44,
                SubtitlePosition = "bottom", BGMVolume = 0.08, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new Template
            {
                Id = 5, Name = "Motivasyon", Description = "İlham verici, epik müzik",
                Type = Domain.Enums.TemplateType.Motivation,
                TTSVoice = "tr-TR-AhmetNeural", TTSRate = 0.9,
                TransitionStyle = "dissolve", UseABSplit = false, IsDefault = false,
                FontFamily = "Arial", FontColor = "#FFFFFF", FontSize = 52,
                SubtitlePosition = "center", BGMVolume = 0.18, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }

    public override int SaveChanges()
    {
        ApplyUserAndTimestamps();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyUserAndTimestamps();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyUserAndTimestamps()
    {
        var uid = _currentUser.Value;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Modified)
                entry.Entity.UpdatedAt = DateTime.UtcNow;

            // Stamp the owner on new rows (unset = 0) so they belong to the current user.
            if (entry.State == EntityState.Added && uid is { } u)
            {
                switch (entry.Entity)
                {
                    case Trend t when t.UserId == 0: t.UserId = u; break;
                    case Video v when v.UserId == 0: v.UserId = u; break;
                    case ChatMessage c when c.UserId == 0: c.UserId = u; break;
                    case TelegramSession s when s.UserId == 0: s.UserId = u; break;
                    case PipelineRun p when p.UserId == 0: p.UserId = u; break;
                    case PublicationStat ps when ps.UserId == 0: ps.UserId = u; break;
                    case ScheduledRun sr when sr.UserId == 0: sr.UserId = u; break;
                }
            }
        }
    }
}
