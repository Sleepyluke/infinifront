namespace SimCore.Sim;

public sealed class ResourceNode
{
    public int Id { get; init; }
    public int CellX { get; init; }
    public int CellY { get; init; }
    public int Remaining { get; set; }
}
