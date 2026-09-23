namespace FantasySentiment.Api.Domain;

public sealed class PlayerAlias
{
    public int Id { get; set; }
    public int PlayerId { get; set; }
    public required string Alias { get; set; }

    public Player Player { get; set; } = null!;
}
