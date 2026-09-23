using FantasySentiment.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FantasySentiment.Api.Data;

public sealed class FantasySentimentDbContext(DbContextOptions<FantasySentimentDbContext> options) : DbContext(options)
{
    public DbSet<Player> Players => Set<Player>();
    public DbSet<PlayerAlias> PlayerAliases => Set<PlayerAlias>();
    public DbSet<RedditPost> RedditPosts => Set<RedditPost>();
    public DbSet<RedditComment> RedditComments => Set<RedditComment>();
    public DbSet<PlayerMention> PlayerMentions => Set<PlayerMention>();
    public DbSet<PlayerSnapshot> PlayerSnapshots => Set<PlayerSnapshot>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Player>(entity =>
        {
            entity.Property(player => player.Name).HasMaxLength(160);
            entity.Property(player => player.Team).HasMaxLength(8);
            entity.Property(player => player.Position).HasMaxLength(8);
            entity.Property(player => player.ExternalId).HasMaxLength(80);
            entity.HasIndex(player => player.Name);
            entity.HasIndex(player => new { player.Team, player.Position });
        });

        modelBuilder.Entity<PlayerAlias>(entity =>
        {
            entity.Property(alias => alias.Alias).HasMaxLength(160);
            entity.HasIndex(alias => new { alias.PlayerId, alias.Alias }).IsUnique();
        });

        modelBuilder.Entity<RedditPost>(entity =>
        {
            entity.Property(post => post.RedditId).HasMaxLength(32);
            entity.Property(post => post.Subreddit).HasMaxLength(80);
            entity.Property(post => post.Title).HasMaxLength(400);
            entity.Property(post => post.Url).HasMaxLength(600);
            entity.HasIndex(post => post.RedditId).IsUnique();
            entity.HasIndex(post => post.CreatedAt);
        });

        modelBuilder.Entity<RedditComment>(entity =>
        {
            entity.Property(comment => comment.RedditId).HasMaxLength(32);
            entity.Property(comment => comment.ParentRedditId).HasMaxLength(32);
            entity.Property(comment => comment.Body).HasMaxLength(4000);
            entity.HasIndex(comment => comment.RedditId).IsUnique();
            entity.HasIndex(comment => comment.CreatedAt);
        });

        modelBuilder.Entity<PlayerMention>(entity =>
        {
            entity.Property(mention => mention.SentimentCategory).HasMaxLength(16);
            entity.Property(mention => mention.Intent).HasMaxLength(16);
            entity.HasIndex(mention => new { mention.PlayerId, mention.CommentId }).IsUnique();
            entity.HasIndex(mention => mention.CreatedAt);
        });

        modelBuilder.Entity<PlayerSnapshot>(entity =>
        {
            entity.HasKey(snapshot => new { snapshot.PlayerId, snapshot.Timestamp });
            entity.HasIndex(snapshot => snapshot.Timestamp);
        });
    }
}
