using SimCore.Math;

namespace SimCore.Sim;

public sealed partial class SimWorld
{
    private readonly System.Collections.Generic.List<ResourceNode> _nodes = new(); // stable order
    private readonly System.Collections.Generic.Dictionary<int, ResourceNode> _nodesById = new(); // lookup only

    public System.Collections.Generic.IReadOnlyList<ResourceNode> Nodes => _nodes;
    public ResourceNode? GetNode(int id) => _nodesById.TryGetValue(id, out var n) ? n : null;

    /// <summary>Setup-time API (map generation). Blocks the cell.</summary>
    public int AddResourceNode(int cellX, int cellY, int amount)
    {
        var n = new ResourceNode { Id = _nextId++, CellX = cellX, CellY = cellY, Remaining = amount };
        _nodes.Add(n);
        _nodesById[n.Id] = n;
        Map.SetPassable(cellX, cellY, false);
        return n.Id;
    }

    private void RemoveNode(ResourceNode n)
    {
        Map.SetPassable(n.CellX, n.CellY, true);
        _nodesById.Remove(n.Id);
        _nodes.Remove(n);
    }

    private bool IsAdjacentToCell(FixVec pos, int cellX, int cellY)
    {
        var (px, py) = Map.WorldToCell(pos);
        return System.Math.Abs(px - cellX) <= 1 && System.Math.Abs(py - cellY) <= 1;
    }

    private bool IsAdjacentToFootprint(FixVec pos, Building b)
    {
        var (px, py) = Map.WorldToCell(pos);
        return px >= b.CellX - 1 && px <= b.CellX + b.Spec.Width
            && py >= b.CellY - 1 && py <= b.CellY + b.Spec.Height;
    }

    /// <summary>Nearest owned completed depot by squared distance; ties → earliest in list (spawn order).</summary>
    private Building? NearestOwnedDepot(Unit u)
    {
        Building? best = null;
        Fix bestDist = default;
        foreach (var b in _buildings)
        {
            if (b.OwnerId != u.OwnerId || !b.IsComplete || !b.Spec.IsDepot || b.Hp <= 0) continue;
            var d = (CenterOf(b) - u.Position).LengthSquared();
            if (best is null || d < bestDist) { best = b; bestDist = d; }
        }
        return best;
    }

    private void IssueApproach(Unit u, FixVec target)
    {
        var (tx, ty) = Map.WorldToCell(target);
        u.HasMoveOrder = true;
        u.MoveTarget = target;
        u.Path = GetField(tx, ty);
        u.PathVersion = Map.Version;
    }

    private void UpdateHarvest()
    {
        foreach (var u in _units)
        {
            if (u.Harvester is null || u.HarvestPhase == HarvestPhase.None) continue;
            var node = GetNode(u.HarvestNodeId);
            switch (u.HarvestPhase)
            {
                case HarvestPhase.MovingToNode:
                    if (node is null || node.Remaining <= 0)
                    {
                        u.HarvestPhase = u.CarriedMinerals > 0 ? HarvestPhase.Returning : HarvestPhase.None;
                        break;
                    }
                    if (IsAdjacentToCell(u.Position, node.CellX, node.CellY))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                        u.HarvestPhase = HarvestPhase.Gathering;
                        u.GatherTicksRemaining = u.Harvester.GatherTicks;
                    }
                    else if (!u.HasMoveOrder)
                    {
                        IssueApproach(u, Map.CellCenter(node.CellX, node.CellY));
                    }
                    break;

                case HarvestPhase.Gathering:
                    if (node is null) { u.HarvestPhase = u.CarriedMinerals > 0 ? HarvestPhase.Returning : HarvestPhase.None; break; }
                    u.GatherTicksRemaining--;
                    if (u.GatherTicksRemaining > 0) break;
                    var take = System.Math.Min(u.Harvester.CarryCapacity, node.Remaining);
                    node.Remaining -= take;
                    u.CarriedMinerals = take;
                    if (node.Remaining <= 0) RemoveNode(node);
                    u.HarvestPhase = HarvestPhase.Returning;
                    break;

                case HarvestPhase.Returning:
                    var depot = NearestOwnedDepot(u);
                    if (depot is null) break; // no depot yet — wait in place
                    if (IsAdjacentToFootprint(u.Position, depot))
                    {
                        u.HasMoveOrder = false;
                        u.Path = null;
                        _players[u.OwnerId].Minerals += u.CarriedMinerals;
                        u.CarriedMinerals = 0;
                        u.HarvestPhase = GetNode(u.HarvestNodeId) is { Remaining: > 0 }
                            ? HarvestPhase.MovingToNode
                            : HarvestPhase.None;
                    }
                    else if (!u.HasMoveOrder)
                    {
                        IssueApproach(u, CenterOf(depot));
                    }
                    break;
            }
        }
    }
}
