using SimCore.Math;
using SimCore.Sim;
using Xunit;

public class PatrolTests
{
    private static Weapon Gun() => new() { Damage = 5, Range = Fix.FromInt(3), CooldownTicks = 4 };

    [Fact]
    public void Patrol_Loops_Between_Both_Points()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        bool reachedB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx == 15) reachedB = true;
            if (reachedB && cx == 5) returnedA = true;
        }
        Assert.True(reachedB, "never reached patrol point B");
        Assert.True(returnedA, "never returned to patrol point A");
        Assert.True(w.GetUnit(id)!.IsPatrolling); // still looping
    }

    [Fact]
    public void Patrolling_Unit_Engages_And_Resumes()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var victim = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(victim));            // engaged and killed en route
        Assert.True(w.GetUnit(id)!.IsPatrolling);  // resumed the loop
    }

    [Fact]
    public void Passive_Patrol_Walks_Without_Engaging()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var bystander = w.SpawnUnit(1, w.Map.CellCenter(10, 6), Fix.FromFraction(1, 2), 10);
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.Passive) });
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        for (int i = 0; i < 100; i++) w.Step(System.Array.Empty<Command>());
        Assert.NotNull(w.GetUnit(bystander));      // untouched
    }

    [Fact]
    public void New_Order_Cancels_Patrol()
    {
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        w.Step(new Command[] { new MoveCommand(0, new[] { id }, w.Map.CellCenter(20, 20)) });
        Assert.False(w.GetUnit(id)!.IsPatrolling);
    }

    [Fact]
    public void Blocked_Endpoint_Patrol_Still_Loops()
    {
        // Park an idle unit ON patrol point B. The patroller must still swap legs (one cell short is fine).
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        // Blocker sits at B — same owner (0) so the armed patroller never attacks it,
        // ensuring the blocked-path logic is what gets exercised, not the kill path.
        w.SpawnUnit(0, w.Map.CellCenter(15, 5), Fix.FromFraction(1, 2), 50);

        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        bool reachedNearB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx >= 13) reachedNearB = true;  // got close to B (one cell short is fine)
            if (reachedNearB && cx <= 7) returnedA = true;
        }
        Assert.True(reachedNearB, "never got close to patrol point B");
        Assert.True(returnedA, "never returned toward patrol point A");
        Assert.True(w.GetUnit(id)!.IsPatrolling);
    }

    [Fact]
    public void Armed_Patroller_RelaxClear_At_Chebyshev2_Does_Not_Livelock()
    {
        // Regression for the Rule-B livelock: ShouldRelaxArrival Rule B can relax-clear the
        // move order when the patroller is at Chebyshev 2 of the endpoint (unit behind a
        // stationary blocker that is itself Chebyshev 1 of the target). The old combat branch
        // only accepted relaxedArrival at Chebyshev ≤ 1, so it re-issued the march every tick
        // while MoveUnits immediately re-cleared it — a permanent livelock.
        //
        // Geometry: patrol A=(5,5) → B=(14,5).
        // Same-owner idle blockers at (13,5) and (14,5).
        //   • Patroller approaching from left tries to enter (13,5); blocker there is at
        //     Chebyshev 1 of target (14,5) → Rule B fires → relax-clear at the patroller's
        //     current cell, which is Chebyshev 2 of (14,5).
        // After fix: combat branch sees relaxedArrival (Chebyshev ≤ 2) → swap legs → patroller
        // heads back toward A and reaches within Chebyshev 2 of A within the tick budget.
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        // Blockers: (14,5) is the endpoint; (13,5) is Chebyshev 1 of endpoint — triggers Rule B.
        w.SpawnUnit(0, w.Map.CellCenter(14, 5), Fix.FromFraction(1, 2), 50); // sits ON endpoint
        w.SpawnUnit(0, w.Map.CellCenter(13, 5), Fix.FromFraction(1, 2), 50); // Chebyshev 1 of endpoint

        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(14, 5)) });
        bool legSwapped = false;
        for (int i = 0; i < 600; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            // Patrol swap occurred if patroller is heading back toward A and is close to it.
            if (cx <= 7) { legSwapped = true; break; }
        }
        Assert.True(legSwapped, "patroller livelocked near B — Rule-B relax-clear at Chebyshev 2 was not accepted");
        Assert.True(w.GetUnit(id)!.IsPatrolling, "patrol should still be active after leg swap");
    }

    [Fact]
    public void SetStance_Passive_MidFight_PatrollerStandsDownAndResumesLoop()
    {
        // Reviewer repro: armed patroller engages an enemy en route, SetStance(Passive) mid-fight,
        // asserts: target cleared, enemy survives, patroller resumes the patrol loop.
        // Without the fix: IsAttackMoving is cleared but AttackTargetId stays set, so UpdateCombat
        // never re-issues the move order; the patroller idles indefinitely and kills the enemy.
        //
        // Enemy is placed one cell OFF the patrol axis (at (8,6)) so it doesn't permanently block
        // the passive patroller's path after stand-down. It is within weapon range (3 cells) of
        // the patroller at (6,5) — distance ≈ sqrt(4+1) < 3 — so acquisition fires.
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        // Enemy with moderate HP near but off the patrol axis — within range but doesn't block path.
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(8, 6), Fix.FromFraction(0, 1), 30);

        // Issue patrol A=(5,5) → B=(15,5).
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });

        // Advance 2 ticks so the patroller moves toward B and acquires the enemy.
        w.Step(System.Array.Empty<Command>());
        w.Step(System.Array.Empty<Command>());

        // SetStance to Passive mid-fight.
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.Passive) });

        // Target must be cleared immediately.
        Assert.Equal(0, w.GetUnit(id)!.AttackTargetId);

        // Enemy must survive (Passive patroller does not attack).
        Assert.NotNull(w.GetUnit(enemy));

        // Run until the patroller has looped: it must reach both endpoints within the budget.
        bool reachedB = false, returnedA = false;
        for (int i = 0; i < 400; i++)
        {
            w.Step(System.Array.Empty<Command>());
            var (cx, _) = w.Map.WorldToCell(w.GetUnit(id)!.Position);
            if (cx >= 13) reachedB = true;
            if (reachedB && cx <= 7) { returnedA = true; break; }
        }
        Assert.True(reachedB, "patroller never resumed march toward B after Passive stand-down");
        Assert.True(returnedA, "patroller never returned toward A — patrol loop broken");
        Assert.True(w.GetUnit(id)!.IsPatrolling, "patrol must still be active");
        Assert.NotNull(w.GetUnit(enemy)); // enemy survived the entire run
    }

    [Fact]
    public void SetStance_Passive_MidPatrol_Stops_Acquiring_And_AutoAttack_Resumes()
    {
        // An armed patroller switched to Passive mid-patrol must stop engaging enemies.
        // Switching back to AutoAttack must resume engagement.
        var w = new SimWorld(new MapGrid(30, 30), seed: 1);
        var id = w.SpawnUnit(0, w.Map.CellCenter(5, 5), Fix.FromFraction(1, 2), 50, Gun());
        var enemy = w.SpawnUnit(1, w.Map.CellCenter(10, 5), Fix.FromFraction(0, 1), 50); // stationary, in patrol route

        // Set Passive BEFORE issuing patrol so the PatrolCommand inherits the passive stance,
        // and then verify that switching to AutoAttack mid-patrol resumes engagement.
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.Passive) });
        w.Step(new Command[] { new PatrolCommand(0, new[] { id }, w.Map.CellCenter(15, 5)) });
        Assert.False(w.GetUnit(id)!.IsAttackMoving, "Passive patrol must not be attack-moving");
        Assert.True(w.GetUnit(id)!.IsPatrolling, "patrol must be active");

        // Run 80 ticks: enemy must survive (passive patroller does not engage).
        for (int i = 0; i < 80; i++) w.Step(System.Array.Empty<Command>());
        Assert.NotNull(w.GetUnit(enemy)); // enemy alive — passive patrol never engaged

        // Switch to AutoAttack mid-patrol — re-derive must set IsAttackMoving=true.
        w.Step(new Command[] { new SetStanceCommand(0, new[] { id }, Stance.AutoAttack) });
        Assert.True(w.GetUnit(id)!.IsAttackMoving, "AutoAttack patrol must set IsAttackMoving");
        Assert.True(w.GetUnit(id)!.IsPatrolling, "patrol must still be active after stance switch");

        // Run another 80 ticks: patroller should now engage and kill the enemy.
        for (int i = 0; i < 80; i++) w.Step(System.Array.Empty<Command>());
        Assert.Null(w.GetUnit(enemy)); // enemy killed — AutoAttack patrol engaged
    }
}
