namespace FantasySentiment.Api.Domain;

public sealed class Player
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Team { get; set; }
    public required string Position { get; set; }
    public string? ExternalId { get; set; }
    public bool IsActive { get; set; } = true;

    public List<PlayerAlias> Aliases { get; set; } = [];
    public List<PlayerMention> Mentions { get; set; } = [];
    public List<PlayerSnapshot> Snapshots { get; set; } = [];
}
