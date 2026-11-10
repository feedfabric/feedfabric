using Microsoft.EntityFrameworkCore;
using FeedFabric.Application.Services.ApiKeys;
using FeedFabric.Application.Services.Comments;
using FeedFabric.Application.Services.DataTypes;
using FeedFabric.Application.Services.Favourites;
using FeedFabric.Application.Services.Follows;
using FeedFabric.Application.Services.Likes;
using FeedFabric.Application.Services.Media;
using FeedFabric.Application.Services.Mentions;
using FeedFabric.Application.Services.Messaging;
using FeedFabric.Application.Services.Ranking;
using FeedFabric.Application.Services.Ranking.Profiles;
using FeedFabric.Application.Services.Safety;
using FeedFabric.Application.Services.Tags;
using FeedFabric.Application.Services.Users;

public class AppDbContext : DbContext
{
     public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<UserEntity> Users => Set<UserEntity>();

    public DbSet<PostEntity> Posts => Set<PostEntity>();

    public DbSet<FollowEntity> Follows => Set<FollowEntity>();

    public DbSet<FavouriteEntity> Favourites => Set<FavouriteEntity>();

    public DbSet<LikeEntity> Likes => Set<LikeEntity>();

    public DbSet<CommentEntity> Comments => Set<CommentEntity>();

    public DbSet<PostTagEntity> PostTags => Set<PostTagEntity>();

    public DbSet<MentionEntity> Mentions => Set<MentionEntity>();

    public DbSet<MediaEntity> Media => Set<MediaEntity>();

    public DbSet<PostMediaEntity> PostMedia => Set<PostMediaEntity>();

    public DbSet<FeedLogEntity> FeedLog => Set<FeedLogEntity>();

    public DbSet<ModerationEventEntity> ModerationEvents => Set<ModerationEventEntity>();

    public DbSet<ReportEntity> Reports => Set<ReportEntity>();

    public DbSet<RankingProfileEntity> RankingProfiles => Set<RankingProfileEntity>();

    public DbSet<TopicSetEntity> TopicSets => Set<TopicSetEntity>();

    public DbSet<ApiKeyEntity> ApiKeys => Set<ApiKeyEntity>();

    public DbSet<DataTypeEntity> DataTypes => Set<DataTypeEntity>();

    public DbSet<OutboxMessageEntity> OutboxMessages => Set<OutboxMessageEntity>();

    public DbSet<ProcessedEventEntity> ProcessedEvents => Set<ProcessedEventEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.ExternalUserId)
            .IsUnique();

        // Serves @mention lookups, and makes sure an @mention means one user
        modelBuilder.Entity<UserEntity>()
            .HasIndex(u => u.Username)
            .IsUnique();

        // Author ID only, no navigation; the foreign key keeps AuthorId pointing at a real user
        modelBuilder.Entity<PostEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(p => p.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves "posts by this user, newest first"; replaces the plain AuthorId index EF would add
        modelBuilder.Entity<PostEntity>()
            .HasIndex(p => new { p.AuthorId, p.Id });

        // Serves "posts in the last N minutes" (dashboard, embedding backfill). Rows arrive in CreatedAt order, so a
        // BRIN index does it at a fraction of a B-tree's size and write cost; the same goes for Likes and Comments.
        modelBuilder.Entity<PostEntity>()
            .HasIndex(p => p.CreatedAt)
            .HasMethod("brin");

        modelBuilder.Entity<PostEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(p => p.RepostOfId)
            .OnDelete(DeleteBehavior.Restrict);

        // One live repost per user per post, and serves repost counts
        modelBuilder.Entity<PostEntity>()
            .HasIndex(p => new { p.RepostOfId, p.AuthorId })
            .IsUnique()
            .HasFilter("\"RepostOfId\" IS NOT NULL AND \"DeletedAt\" IS NULL");

        modelBuilder.Entity<FollowEntity>()
            .HasKey(f => new { f.FollowerId, f.FolloweeId });

        // The key serves "my favourites"; nothing asks who favourited someone, so there's no reverse index
        modelBuilder.Entity<FavouriteEntity>()
            .HasKey(f => new { f.UserId, f.FavouriteId });

        modelBuilder.Entity<FavouriteEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FavouriteEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(f => f.FavouriteId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves fan-out: "followers of this author", paged by FollowerId
        modelBuilder.Entity<FollowEntity>()
            .HasIndex(f => new { f.FolloweeId, f.FollowerId });

        modelBuilder.Entity<FollowEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(f => f.FollowerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FollowEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(f => f.FolloweeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LikeEntity>()
            .HasKey(l => new { l.UserId, l.PostId });

        // Serves "who liked this post"
        modelBuilder.Entity<LikeEntity>()
            .HasIndex(l => new { l.PostId, l.UserId });

        // Serves "what this user liked lately": the viewer's taste vector and For You's similar readers
        modelBuilder.Entity<LikeEntity>()
            .HasIndex(l => new { l.UserId, l.CreatedAt })
            .IncludeProperties(l => l.PostId);

        modelBuilder.Entity<LikeEntity>()
            .HasIndex(l => l.CreatedAt)
            .HasMethod("brin");

        modelBuilder.Entity<LikeEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<LikeEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(l => l.PostId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves "comments on this post" (ParentCommentId null) in order
        modelBuilder.Entity<CommentEntity>()
            .HasIndex(c => new { c.PostId, c.ParentCommentId, c.Id });

        // Serves "replies to this comment" in order, and reply counts
        modelBuilder.Entity<CommentEntity>()
            .HasIndex(c => new { c.ParentCommentId, c.Id });

        // Serves "what this user commented on lately"; replaces the plain AuthorId index EF would add
        modelBuilder.Entity<CommentEntity>()
            .HasIndex(c => new { c.AuthorId, c.CreatedAt });

        modelBuilder.Entity<CommentEntity>()
            .HasIndex(c => c.CreatedAt)
            .HasMethod("brin");

        modelBuilder.Entity<CommentEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(c => c.PostId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CommentEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(c => c.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CommentEntity>()
            .HasOne<CommentEntity>()
            .WithMany()
            .HasForeignKey(c => c.ParentCommentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PostTagEntity>()
            .HasKey(t => new { t.PostId, t.Tag });

        // Serves tag pages, newest first
        modelBuilder.Entity<PostTagEntity>()
            .HasIndex(t => new { t.Tag, t.PostId });

        // Serves trending: recent tags only
        modelBuilder.Entity<PostTagEntity>()
            .HasIndex(t => t.CreatedAt);

        modelBuilder.Entity<PostTagEntity>()
            .Property(t => t.Tag)
            .HasMaxLength(50);

        modelBuilder.Entity<PostTagEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(t => t.PostId)
            .OnDelete(DeleteBehavior.Cascade);

        // Serves "where was I mentioned", newest first
        modelBuilder.Entity<MentionEntity>()
            .HasIndex(m => new { m.MentionedUserId, m.Id });

        modelBuilder.Entity<MentionEntity>()
            .HasIndex(m => new { m.PostId, m.CommentId });

        modelBuilder.Entity<MentionEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(m => m.MentionedUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MentionEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(m => m.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MentionEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(m => m.PostId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MentionEntity>()
            .HasOne<CommentEntity>()
            .WithMany()
            .HasForeignKey(m => m.CommentId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MediaEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(m => m.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<MediaEntity>()
            .Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(20);

        // Serves "this user's media" and stale-upload cleanup
        modelBuilder.Entity<MediaEntity>()
            .HasIndex(m => new { m.OwnerId, m.Status });

        modelBuilder.Entity<PostMediaEntity>()
            .HasKey(pm => new { pm.PostId, pm.MediaId });

        // An image can only be on one post
        modelBuilder.Entity<PostMediaEntity>()
            .HasIndex(pm => pm.MediaId)
            .IsUnique();

        modelBuilder.Entity<PostMediaEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(pm => pm.PostId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<PostMediaEntity>()
            .HasOne<MediaEntity>()
            .WithMany()
            .HasForeignKey(pm => pm.MediaId)
            .OnDelete(DeleteBehavior.Restrict);

        // Serves everything that reads one viewer's recent rows: label updates ("this viewer just liked this post,
        // served in the last hour"), "seen in the last day" and the ranker's per-author and per-session history. The
        // time range narrows it first, so it stays fast however long the viewer's history gets; PostId is checked in
        // the index without visiting the table.
        modelBuilder.Entity<FeedLogEntity>()
            .HasIndex(f => new { f.ViewerId, f.ServedAt, f.PostId });

        // Serves the dashboard's windows and training exports
        modelBuilder.Entity<FeedLogEntity>()
            .HasIndex(f => f.ServedAt);

        modelBuilder.Entity<FeedLogEntity>()
            .Property(f => f.Features)
            .HasColumnType("jsonb");

        modelBuilder.Entity<FeedLogEntity>().Property(f => f.Feed).HasMaxLength(16);
        modelBuilder.Entity<FeedLogEntity>().Property(f => f.Ranker).HasMaxLength(64);
        modelBuilder.Entity<FeedLogEntity>().Property(f => f.Profile).HasMaxLength(64);

        // One row per saved version; the latest is the live one
        modelBuilder.Entity<RankingProfileEntity>()
            .HasIndex(p => new { p.Key, p.Version })
            .IsUnique();
        modelBuilder.Entity<RankingProfileEntity>().Property(p => p.Key).HasMaxLength(RankingProfileStore.MaxKeyLength);
        modelBuilder.Entity<RankingProfileEntity>().Property(p => p.Settings).HasColumnType("jsonb");
        modelBuilder.Entity<RankingProfileEntity>().Property(p => p.SavedBy).HasMaxLength(64);

        // One row per saved version of the topics; the latest is the live one
        modelBuilder.Entity<DataTypeEntity>().HasIndex(t => t.Key).IsUnique();
        modelBuilder.Entity<DataTypeEntity>().Property(t => t.Key).HasMaxLength(DataTypeStore.MaxKeyLength);
        modelBuilder.Entity<DataTypeEntity>().Property(t => t.Label).HasMaxLength(DataTypeStore.MaxLabelLength);
        modelBuilder.Entity<DataTypeEntity>().Property(t => t.Description).HasMaxLength(DataTypeStore.MaxDescriptionLength);
        modelBuilder.Entity<DataTypeEntity>().Property(t => t.Schema).HasColumnType("jsonb");
        modelBuilder.Entity<DataTypeEntity>().Property(t => t.UpdatedBy).HasMaxLength(64);

        modelBuilder.Entity<PostEntity>().Property(p => p.DataType).HasMaxLength(DataTypeStore.MaxKeyLength);
        modelBuilder.Entity<PostEntity>().Property(p => p.Data).HasColumnType("jsonb");
        // Serves "this member's posts of one data type" (GET /userfeed?type=), and only covers posts that have one
        modelBuilder.Entity<PostEntity>()
            .HasIndex(p => new { p.AuthorId, p.DataType, p.Id })
            .HasFilter("\"DataType\" IS NOT NULL");

        // Keys are looked up by hash on every request they're used for
        modelBuilder.Entity<ApiKeyEntity>().HasIndex(k => k.Hash).IsUnique();
        modelBuilder.Entity<ApiKeyEntity>().Property(k => k.Hash).HasMaxLength(64);
        modelBuilder.Entity<ApiKeyEntity>().Property(k => k.Name).HasMaxLength(ApiKeyStore.MaxNameLength);
        modelBuilder.Entity<ApiKeyEntity>().Property(k => k.Prefix).HasMaxLength(ApiKeyStore.PrefixLength);
        modelBuilder.Entity<ApiKeyEntity>().Property(k => k.CreatedBy).HasMaxLength(64);
        modelBuilder.Entity<ApiKeyEntity>().Property(k => k.RevokedBy).HasMaxLength(64);

        modelBuilder.Entity<TopicSetEntity>().HasIndex(t => t.Version).IsUnique();
        modelBuilder.Entity<TopicSetEntity>().Property(t => t.Topics).HasColumnType("jsonb");
        modelBuilder.Entity<TopicSetEntity>().Property(t => t.Library).HasMaxLength(TopicStore.MaxLibraryLength);
        modelBuilder.Entity<TopicSetEntity>().Property(t => t.SavedBy).HasMaxLength(64);

        modelBuilder.Entity<PostEntity>().Property(p => p.Status).HasConversion<string>().HasMaxLength(16);
        modelBuilder.Entity<CommentEntity>().Property(c => c.Status).HasConversion<string>().HasMaxLength(16);

        modelBuilder.Entity<ModerationEventEntity>().HasIndex(m => m.CreatedAt);
        modelBuilder.Entity<ModerationEventEntity>().HasIndex(m => new { m.UserId, m.CreatedAt });
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.Kind).HasMaxLength(16);
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.Category).HasMaxLength(32);
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.Excerpt).HasMaxLength(120);
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.Action).HasMaxLength(16);
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.Decision).HasMaxLength(16);
        modelBuilder.Entity<ModerationEventEntity>().Property(m => m.ReviewedBy).HasMaxLength(64);
        // Serves the review queue: what's waiting, oldest first
        modelBuilder.Entity<ModerationEventEntity>()
            .HasIndex(m => m.CreatedAt, "IX_ModerationEvents_Open")
            .HasFilter("\"ReviewedAt\" IS NULL AND \"Action\" <> 'blocked'");
        modelBuilder.Entity<ModerationEventEntity>().HasIndex(m => new { m.PostId, m.CommentId });

        // One report per person per post or comment; a post's own reports have CommentId null
        modelBuilder.Entity<ReportEntity>()
            .HasIndex(r => new { r.PostId, r.CommentId, r.ReporterId })
            .IsUnique()
            .AreNullsDistinct(false);
        modelBuilder.Entity<ReportEntity>().Property(r => r.Reason).HasMaxLength(16);
        modelBuilder.Entity<ReportEntity>()
            .HasOne<UserEntity>()
            .WithMany()
            .HasForeignKey(r => r.ReporterId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReportEntity>()
            .HasOne<PostEntity>()
            .WithMany()
            .HasForeignKey(r => r.PostId)
            .OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<ReportEntity>()
            .HasOne<CommentEntity>()
            .WithMany()
            .HasForeignKey(r => r.CommentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Only unpublished rows are indexed, so the publisher's poll stays fast as the table grows
        modelBuilder.Entity<OutboxMessageEntity>()
            .HasIndex(o => o.CreatedAt)
            .HasFilter("\"ProcessedAt\" IS NULL");

        modelBuilder.Entity<ProcessedEventEntity>()
            .HasKey(e => new { e.EventId, e.Consumer });

        // Serves the dashboard's "when did each consumer last finish an event" without reading the whole table
        modelBuilder.Entity<ProcessedEventEntity>()
            .HasIndex(e => new { e.Consumer, e.ProcessedAt });
    }
}
