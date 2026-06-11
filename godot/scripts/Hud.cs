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
    private (bool workerSel, int buildingId, bool canTrain) _lastButtonKey;

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
        _resources.Text = $"Minerals {p.Minerals}   Supply {p.SupplyUsed}/{p.SupplyCap}" +
                          (_runner.Paused ? "   [PAUSED]" : "");
        _playerBadge.Text = $"Commanding: Player {_sel.ControlledPlayer + 1}";
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

    private void CheckButtonRelevance()
    {
        var w = _runner.World;
        bool workerSel = _sel.SelectedUnits.Select(w.GetUnit).Any(u => u?.Harvester is not null);
        var bld = _sel.SelectedBuilding != 0 ? w.GetBuilding(_sel.SelectedBuilding) : null;
        bool canTrain = bld is { IsComplete: true, Spec.CanTrain: true };
        var key = (workerSel, _sel.SelectedBuilding, canTrain);
        if (key != _lastButtonKey) RebuildButtons();
    }

    private void RebuildButtons()
    {
        foreach (var c in _buttons.GetChildren()) c.QueueFree();
        var w = _runner.World;
        var p = _sel.ControlledPlayer;

        bool workerSelected = _sel.SelectedUnits.Select(w.GetUnit).Any(u => u?.Harvester is not null);
        if (workerSelected)
        {
            AddButton($"Build Depot ({ReferenceSpecs.Depot.MineralCost})",
                () => _cmd.ArmBuildGhost(ReferenceSpecs.Depot));
            AddButton($"Build Barracks ({ReferenceSpecs.Barracks.MineralCost})",
                () => _cmd.ArmBuildGhost(ReferenceSpecs.Barracks));
        }

        if (_sel.SelectedBuilding != 0 && w.GetBuilding(_sel.SelectedBuilding) is { IsComplete: true, Spec.CanTrain: true } b)
        {
            AddButton($"Trooper ({ReferenceSpecs.Trooper.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Trooper)));
            AddButton($"Outrider ({ReferenceSpecs.Outrider.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Outrider)));
            AddButton($"Tank ({ReferenceSpecs.Tank.MineralCost})",
                () => _runner.Enqueue(new TrainCommand(p, b.Id, ReferenceSpecs.Tank)));
        }

        // Record what we just built so CheckButtonRelevance can skip redundant rebuilds.
        bool workerSel = workerSelected;
        var bld = _sel.SelectedBuilding != 0 ? w.GetBuilding(_sel.SelectedBuilding) : null;
        bool canTrain = bld is { IsComplete: true, Spec.CanTrain: true };
        _lastButtonKey = (workerSel, _sel.SelectedBuilding, canTrain);
    }

    private void AddButton(string text, System.Action onPress)
    {
        var btn = new Button { Text = text };
        btn.FocusMode = Control.FocusModeEnum.None;
        btn.Pressed += onPress;
        _buttons.AddChild(btn);
    }
}
