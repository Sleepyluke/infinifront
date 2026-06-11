using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class FogGatingTests
{
    private static Weapon TestWeapon() =>
        new() { Damage = 5, Range = Fix.FromInt(4), CooldownTicks = 3 };

    [Fact]
    public void AttackMove_Ignores_Enemies_In_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        // enemy outside sight (legacy spawn default sight 7); within acquire range never matters — fog wins
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(10, 5)) });
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Explicit_Attack_On_Fogged_Target_Is_Rejected()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Chase_Drops_When_Target_Escapes_Into_Fog()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 8), 50, TestWeapon());
        var runner = w.SpawnUnit(1, w.Map.CellCenter(9, 5), Fix.FromFraction(1, 2), 500);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, runner) });
        Assert.Equal(runner, w.GetUnit(attacker)!.AttackTargetId);
        w.Step(new Command[] { new MoveCommand(1, new[] { runner }, w.Map.CellCenter(35, 5)) });
        for (int i = 0; i < 150; i++) w.Step(System.Array.Empty<Command>());
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId); // lost in the fog
    }

    [Fact]
    public void FogDisabled_Restores_Omniscient_Acquisition()
    {
        var w = new SimWorld(new MapGrid(40, 40), seed: 1) { FogEnabled = false };
        var attacker = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, TestWeapon());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(30, 5), Fix.FromFraction(1, 2), 50);
        w.Step(new Command[] { new AttackCommand(0, new[] { attacker }, enemy) });
        Assert.Equal(enemy, w.GetUnit(attacker)!.AttackTargetId);
    }

    [Fact]
    public void Acquisition_Skips_Enemy_Within_Acquire_Range_But_Beyond_Sight()
    {
        // weapon range 6 → acquire range 8 (AcquireBonus=2); legacy spawn sight 7.
        //
        // Setup: one visible enemy (cell 6,5) that dies in the first shot, plus one fogged enemy
        // (cell 13,5) at world-distance 8 — exactly at the acquire boundary, one cell past sight.
        //
        // After the near enemy dies the attacker clears its target and calls AcquireTarget again.
        // At that point HasMoveOrder is false (cleared when the attacker stopped to fire), so the
        // march-resume branch  (else if !HasMoveOrder …)  will run IFF AcquireTarget returns 0.
        //
        // Without the IsVisibleTo skip in AcquireTarget: the fogged far enemy is acquired (d²=64,
        // rangeSq=64, 64 > 64 is false), the fog chase-drop clears it, and the continue skips the
        // march-resume branch — HasMoveOrder stays false and the attacker never resumes marching.
        // With the skip present: AcquireTarget returns 0, march-resume fires, HasMoveOrder=true.
        var w = new SimWorld(new MapGrid(40, 40), seed: 1);
        var longGun = new Weapon { Damage = 5, Range = Fix.FromInt(6), CooldownTicks = 3 };
        var attacker    = w.SpawnUnit(0, w.Map.CellCenter(5,  5), Fix.FromFraction(1, 2), 50, longGun);
        var nearEnemy   = w.SpawnUnit(1, w.Map.CellCenter(6,  5), Fix.FromFraction(1, 2),  1); // 1 hp — one-shot
        var farEnemy    = w.SpawnUnit(1, w.Map.CellCenter(13, 5), Fix.FromFraction(1, 2), 50); // 8 world-units: > sight 7, <= acquire 8
        // Step 1: attack-move east — attacker acquires and kills nearEnemy in the same tick.
        w.Step(new Command[] { new AttackMoveCommand(0, new[] { attacker }, w.Map.CellCenter(20, 5)) });
        Assert.Null(w.GetUnit(nearEnemy));                                   // near enemy dead and removed
        // Step 2: march should resume (fog-skip lets else-if run); fogged far enemy not acquired.
        w.Step(System.Array.Empty<Command>());
        Assert.True(w.GetUnit(attacker)!.HasMoveOrder,                       // march re-issued by else-if
            "attacker did not resume marching — fog-skip in AcquireTarget may be missing");
        Assert.Equal(0, w.GetUnit(attacker)!.AttackTargetId);                // fogged far enemy not acquired
        // Extend sight so the far enemy becomes visible; should be acquired in the next step.
        w.GetUnit(attacker)!.SightRange = 12;                                // sim-test backdoor
        w.Step(System.Array.Empty<Command>());
        Assert.Equal(farEnemy, w.GetUnit(attacker)!.AttackTargetId);         // visible → acquired
    }
}
