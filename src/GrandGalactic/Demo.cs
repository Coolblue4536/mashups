using System.Numerics;

namespace GrandGalactic;

/// <summary>For --screenshot: makes a few ordinary player moves at the start of a real run (work the homeworld, farm,
/// survey a system, research) so a capture shows the mashup being played rather than an untouched board.</summary>
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
        On("uncharted_system", "science_ship");
        On("research", "scientist");
        On("research", "scientist");
        if (Find("minerals") != null && Find("construction_ship") is { } cs)
            for (int i = 0; i < 3 && Find("minerals") is { } m; i++) sim.StackOnto(m, cs);
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
    }
}
