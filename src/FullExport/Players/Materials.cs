namespace MoreFortniteReplayDecompression.Players;

/// <summary>An amount of building materials per type.</summary>
public readonly record struct Materials(int Wood, int Stone, int Metal)
{
    public int Total => Wood + Stone + Metal;

    /// <summary>Adds <paramref name="amount"/> of <paramref name="material"/>.</summary>
    public Materials Add(BuildMaterial material, int amount)
    {
        return material switch
        {
            BuildMaterial.Wood => this with { Wood = Wood + amount },
            BuildMaterial.Stone => this with { Stone = Stone + amount },
            _ => this with { Metal = Metal + amount },
        };
    }
}
