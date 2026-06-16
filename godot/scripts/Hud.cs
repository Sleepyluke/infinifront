using System.Linq;
using Godot;
using SimCore.Sim;

namespace LlmRts.Godot;

public partial class Hud : CanvasLayer
{
    private SimRunner _runner = null!;
    private SelectionController _sel = null!;
    private CommandController _cmd = null!;

    private Label _resources = null!;
    private Label _playerBadge = null!;
    private Label _selectionInfo = null!;
    private HBoxContainer _buttons = null!;
    private ProgressBar _queueBar = null!;

    // Stale-button detection: tracks last state that RebuildButtons rendered from.
    // commonStance: null = mixed/no armed units, non-null = the stance shared by all armed units.
    private (bool workerSel, int buildingId, bool canTrain, Stance? commonStance) _lastButtonKey;

    public void Init(SimRunner runner, SelectionController sel, CommandController cmd)
    {
        _runner = runner; _sel = sel; _cmd = cmd;

        _playerBadge = new Label { Position = new Vector2(12, 8) };
        AddChild(_playerBadge);

        _resources = new Label { AnchorLeft = 1, AnchorRight = 1, OffsetLeft = -260, OffsetTop = 8 };
        AddChild(_resources);

        var panel = new PanelContainer
        {
            AnchorTop = 1, AnchorBottom = 1, AnchorLeft = 0, AnchorRight = 1,
            OffsetTop = -86, OffsetLeft = 8, OffsetRight = -8, OffsetBottom = -8,
        };
        AddChild(panel);
        var row = new HBoxContainer();
        panel.AddChild(row);
        _selectionInfo = new Label { CustomMinimumSize = new Vector2(320, 0) };
        row.AddChild(_selectionInfo);
        _buttons = new HBoxContainer();
        row.AddChild(_buttons);
        _queueBar = new ProgressBar { CustomMinimumSize = new Vector2(140, 16), Visible = false, MaxValue = 1.0 };
        row.AddChild(_queueBar);

        _sel.SelectionChanged += RebuildButtons;
        _sel.PlayerSwitched += RebuildButtons;
        runner.Ticked += CheckButtonRelevance;
        RebuildButtons();
    }

    public override void _Process(double delta)
    {
        var p = _runner.World.Players[_sel.ControlledPlayer];
        var fogTag = _runner.World.FogEnabled ? "" : "   [FOG OFF]";
        _resources.Text = $"Minerals {p.Minerals}   Supply {p.SupplyUsed}/{p.SupplyCap}" +
                          (_runner.Paused ? "   [PAUSED]" : "") + fogTag;
        string net = _runner.IsNetworked
            ? $"   ·   NET p{_runner.NetLocalPlayer}  tick {_runner.TickCount}" +
              (_runner.NetStallFrames > 10 ? "  [STALLED — waiting for peer frames]" : "")
            : "";
        _playerBadge.Text = $"Commanding: Player {_sel.ControlledPlayer + 1}{net}";
        _playerBadge.Modulate = UnitView.PlayerColors[_sel.ControlledPlayer];
        UpdateSelectionInfo();
    }

    private void UpdateSelectionInfo()
    {
        var w = _runner.World;
        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { } b)
        {
            var kind = b.Spec.IsDepot ? "Depot" : b.Spec.CanTrain ? "Barracks" : "Building";
            _selectionInfo.Text = $"{kind}  HP {b.Hp}/{b.Spec.MaxHp}" +
                (b.IsComplete ? $"  queue {b.Queue.Count}/{Building.MaxQueueLength}" : "  [constructing]");
            if (b.Queue.Count > 0)
            {
                _queueBar.Visible = true;
                var head = b.Queue[0];
                _queueBar.Value = 1.0 - (double)head.RemainingTicks / head.Spec.BuildTimeTicks;
            }
            else _queueBar.Visible = false;
            return;
        }
        _queueBar.Visible = false;
        if (_sel.SelectedUnits.Count == 0) { _selectionInfo.Text = ""; return; }
        var units = _sel.SelectedUnits.Select(w.GetUnit).Where(u => u is not null).ToList();
        if (units.Count == 1)
        {
            var u = units[0]!;
            var carry = u.Harvester is not null ? $"  carrying {u.CarriedMinerals}" : "";
            _selectionInfo.Text = $"Unit #{u.Id}  HP {u.Hp}{carry}";
        }
        else _selectionInfo.Text = $"{units.Count} units selected";
    }

    private static Stance? CommonStance(System.Collections.Generic.IEnumerable<Unit?> units)
    {
        Stance? common = null;
        foreach (var u in units)
        {
            if (u?.Weapon is null) continue;
            if (common is null) common = u.Stance;
            else if (common != u.Stance) return null; // mixed
        }
        return common; // null if no armed units
    }

    private void CheckButtonRelevance()
    {
        var w = _runner.World;
        var selectedUnits = _sel.SelectedUnits.Select(w.GetUnit).ToList();
        bool workerSel = selectedUnits.Any(u => u?.Harvester is not null);
        var bld = _sel.SelectedBuilding != 0 ? w.GetBuilding(_sel.SelectedBuilding) : null;
        bool canTrain = bld is { IsComplete: true, Spec.CanTrain: true };
        var commonStance = CommonStance(selectedUnits);
        var key = (workerSel, _sel.SelectedBuilding, canTrain, commonStance);
        if (key != _lastButtonKey) RebuildButtons();
    }

    private void RebuildButtons()
    {
        foreach (var c in _buttons.GetChildren()) c.QueueFree();
        var w = _runner.World;
        var p = _sel.ControlledPlayer;

        var selectedUnits = _sel.SelectedUnits.Select(w.GetUnit).ToList();
        bool workerSelected = selectedUnits.Any(u => u?.Harvester is not null);
        if (workerSelected && w.Faction is { } faction)
        {
            foreach (var bdef in faction.BuildingList)
            {
                var capturedDef = bdef; // capture for lambda
                AddButton($"Build {capturedDef.Id} ({capturedDef.Spec.MineralCost})",
                    () => _cmd.ArmBuildGhost(capturedDef), BuildingTip(capturedDef));
            }
        }

        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { IsComplete: true, Spec.CanTrain: true } b
            && w.Faction is { } trainFaction)
        {
            var buildingDefId = b.DefId;
            foreach (var udef in trainFaction.UnitList.Where(u => u.ProducedBy == buildingDefId))
            {
                var capturedUdef = udef; // capture for lambda
                AddButton($"{capturedUdef.Id} ({capturedUdef.Spec.MineralCost})",
                    () => _runner.Enqueue(new TrainCommand(p, b.Id, capturedUdef.Id)), UnitTip(capturedUdef));
            }
        }

        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { IsComplete: true } rb
            && w.Faction is { } researchFaction)
        {
            foreach (var udef in researchFaction.UpgradeList.Where(u => u.ResearchedAt == rb.DefId
                                                                      && !w.Players[p].HasUpgrade(u.Id)))
            {
                var capturedUp = udef;
                AddButton($"Research {capturedUp.Id} ({capturedUp.MineralCost})",
                    () => _runner.Enqueue(new ResearchCommand(p, rb.Id, capturedUp.Id)));
            }
        }

        // Stance buttons: shown when ≥1 owned ARMED unit is selected.
        var armedUnits = selectedUnits.Where(u => u?.Weapon is not null).ToList();
        if (armedUnits.Count > 0)
        {
            var common = CommonStance(armedUnits);
            var ids = _sel.SelectedUnits.ToArray();
            void AddStance(string label, Stance s)
            {
                var prefix = common == s ? ">" : "";
                AddButton(prefix + label, () => _runner.Enqueue(new SetStanceCommand(p, ids, s)));
            }
            AddStance("Auto", Stance.AutoAttack);
            AddStance("Defend", Stance.Defend);
            AddStance("Passive", Stance.Passive);
        }

        // Record what we just built so CheckButtonRelevance can skip redundant rebuilds.
        bool workerSel = workerSelected;
        var bld = _sel.SelectedBuilding != 0 ? w.GetBuilding(_sel.SelectedBuilding) : null;
        bool canTrain = bld is { IsComplete: true, Spec.CanTrain: true };
        var commonStance = CommonStance(selectedUnits);
        _lastButtonKey = (workerSel, _sel.SelectedBuilding, canTrain, commonStance);
    }

    private void AddButton(string text, System.Action onPress, string tooltip = "")
    {
        var btn = new Button { Text = text };
        btn.FocusMode = Control.FocusModeEnum.None;
        if (tooltip.Length > 0) btn.TooltipText = tooltip;
        btn.Pressed += onPress;
        _buttons.AddChild(btn);
    }

    // Hover tooltips: surface the catalog stats so players can compare units/buildings.
    private static string BuildingTip(BuildingDef d)
    {
        var s = d.Spec;
        var parts = new System.Collections.Generic.List<string> { $"Cost {s.MineralCost}", $"HP {s.MaxHp}", $"{s.Width}x{s.Height}" };
        if (s.SupplyProvided > 0) parts.Add($"+{s.SupplyProvided} supply");
        if (s.IsDepot) parts.Add("HQ");
        if (s.CanTrain) parts.Add("trains units");
        if (s.Weapon is { } w) parts.Add($"dmg {w.Damage}/rng {w.Range.ToInt()}");
        return $"{d.Id}\n" + string.Join("  ·  ", parts);
    }

    private static string UnitTip(UnitDef d)
    {
        var s = d.Spec;
        var parts = new System.Collections.Generic.List<string> { $"Cost {s.MineralCost}", $"{s.SupplyCost} supply", $"HP {s.MaxHp}" };
        if (s.Weapon is { } w) parts.Add($"dmg {w.Damage}/rng {w.Range.ToInt()}/cd {w.CooldownTicks}");
        if (s.Harvester is not null) parts.Add("harvester");
        return $"{d.Id}\n" + string.Join("  ·  ", parts);
    }
}
