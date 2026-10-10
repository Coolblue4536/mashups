using System.Numerics;

namespace GrandGalactic;

/// <summary>For --screenshot: makes a few ordinary player moves at the start of a real run (work the homeworld, farm,
/// mine, survey a system, order a building) so a capture shows the mashup being played rather than an untouched board.</summary>
public static class Demo
{
    public static void Setup(Sim sim)
    {
        Stack? Find(string id, Stack? not = null) => sim.StacksIn(sim.Home).FirstOrDefault(s => s != not && s.Cards.Count == 1 && s.Root.Def.Id == id);
        void On(string top, string bottom)
        {
            if (Find(bottom) is { } b && Find(top, b) is { } t) sim.StackOnto(t, b);
        }
        On(sim.Ethic.WorkerCard, "homeworld");
        On(sim.Ethic.WorkerCard, "agriculture_district");
        On(sim.Ethic.WorkerCard, "mining_district");
        On("uncharted_system", "science_ship");
        // A few moons' worth of work in the pool, and a district on order.
        foreach (var (id, n) in new[] { ("energy", 7), ("food", 9), ("minerals", 5), ("alloys", 2), ("research", 3) }) sim.Gain(id, n, sim.Home.Center, made: false);
        if (Find("construction_ship") is { } cs) sim.SetOrder(cs, Defs.Recipes.First(r => r.Id == "b_generator"));
        // A raider in the capital, met by the fleet (Militarist start has a Corvette), fitted the way a player would:
        // the start's ship parts and its Admiral go on board first.
        var raider = sim.Spawn("pirate_raider", sim.Home.Center + new Vector2(450, 120), jitter: false);
        if (Find("corvette") is { } fleet)
        {
            foreach (var part in sim.StacksIn(sim.Home).Where(s => s.Cards.Count == 1 && s.Root.Def.Category == "component").ToList())
                sim.Fit(part.Root, fleet.Root);
            if (Find("admiral") is { } admiral) sim.AssignAdmiral(admiral.Root, fleet.Root);
            sim.Attack(fleet, raider);
        }
        sim.Messages.Clear();
        sim.Gains.Clear();
    }
}
