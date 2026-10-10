namespace GrandGalactic;

/// <summary>Technology and species bonuses, repeatable research, and upkeep.</summary>
public sealed partial class Sim
{
    /// <summary>Every regular blueprint is researched: repeatable research opens.</summary>
    public bool AllBlueprintsKnown => Defs.Cards.Where(c => c.Category == "tech" && !c.HasTag("repeatable")).All(c => Techs.Contains(c.Id));

    public bool TechOk(RecipeDef r) => r.RequiresTech == "none" || (r.RequiresTech == "all" ? AllBlueprintsKnown : Techs.Contains(r.RequiresTech));

    float RepMult(string tech) => 1 + Defs.Rules.RepStepPct / 100f * RepLevels.GetValueOrDefault(tech);

    /// <summary>How much faster (or stronger) something is: known technologies, repeatable levels and the species.
    /// applies: a resource (work making it), learn, babies, build, hull, shields, armor, damage, homeworld, starbase.</summary>
    public float Mult(string applies)
    {
        float m = 1;
        foreach (var b in Defs.Bonuses)
        {
            if (b.Applies != applies) continue;
            if (Defs.Card[b.Tech].HasTag("repeatable")) m *= RepMult(b.Tech);
            else if (Techs.Contains(b.Tech)) m *= b.Mult;
        }
        m *= applies switch
        {
            "food" => Species.FoodMult,
            "minerals" => Species.MineralsMult,
            "energy" => Species.EnergyMult,
            "alloys" => Species.AlloysMult,
            "research" or "learn" => Species.ResearchMult,
            "babies" => Species.BabiesMult,
            _ => 1f,
        };
        if (applies == "learn") m *= RepMult("tech_rep_research");
        return m;
    }

    /// <summary>The resource a recipe makes, if any (for its work-speed bonus).</summary>
    static string? MadeResource(RecipeDef r, Card station)
    {
        foreach (var o in r.Outputs)
            foreach (var g in o.Give)
            {
                var id = g.Card == "station.yield" ? station.Def.Yield : g.Card;
                if (IsResource(id)) return id;
            }
        return null;
    }

    /// <summary>How much faster a recipe runs than its sheet time.</summary>
    float SpeedMult(RecipeDef r, Card station)
    {
        float m = 1;
        if (MadeResource(r, station) is { } res) m *= Mult(res);
        if (r.Effect == "learn" || r.Effect.StartsWith("repeat:")) m *= Mult("learn");
        if (r.Tag == "build") m *= Mult("build");
        if (r.Outputs.Any(o => o.Give.Any(g => g.Card == "baby"))) m *= Mult("babies");
        if (EnergyDeficit) m /= Defs.Rules.EnergyDeficitWorkPct / 100f;
        return m;
    }

    /// <summary>Your ships' damage: technologies and repeatables, less an energy deficit.</summary>
    public float DamageMult => Mult("damage") * (EnergyDeficit ? Defs.Rules.EnergyDeficitDamagePct / 100f : 1f);

    /// <summary>Energy your buildings and fleets cost each moon (Drones are counted with the people).</summary>
    public int StructureUpkeep => AllCards.Where(c => c.Def.Category != "person").Sum(c => c.Def.EnergyUpkeep)
                                  + (War?.Fleet.Where(c => c.Def.Category != "person").Sum(c => c.Def.EnergyUpkeep) ?? 0);

    /// <summary>Max hull, shields and armour from the sheet, fitted parts and bonuses (hostiles: difficulty).</summary>
    void RefreshStats(Card c)
    {
        var d = c.Def;
        float hull = 1, shield = 1, armor = 1;
        if (d.IsHostile) hull = shield = armor = Diff.EnemyHpMult;
        else if (d.HasTag("warship")) { hull = Mult("hull"); shield = Mult("shields"); armor = Mult("armor"); }
        else if (d.Id == "starbase") { hull = Mult("starbase") * Mult("hull"); shield = Mult("shields"); armor = Mult("armor"); }
        else if (d.Id == "homeworld") { hull = shield = Mult("homeworld"); }
        else if (d.Category == "person" && d.HasTag("worker")) hull = Species.PopHpMult;
        c.MaxHp = MathF.Round(d.Hp * hull + c.Parts.Sum(p => p.Hull) * hull);
        c.MaxShield = MathF.Round((d.Shield + c.Parts.Sum(p => p.Shield)) * shield);
        c.MaxArmor = MathF.Round((d.Armor + c.Parts.Sum(p => p.Armor)) * armor);
    }

    /// <summary>After a technology: every card's bonuses and every stack's work time are brought up to date.</summary>
    void RefreshAll()
    {
        foreach (var c in AllCards.Concat(War?.Fleet ?? Enumerable.Empty<Card>()).Where(c => !c.Def.IsHostile).ToList())
        {
            float h = c.MaxHp > 0 ? c.Hp / c.MaxHp : 1, s = c.MaxShield > 0 ? c.Shield / c.MaxShield : 1, a = c.MaxArmor > 0 ? c.Armor / c.MaxArmor : 1;
            RefreshStats(c);
            c.Hp = c.MaxHp * h; c.Shield = c.MaxShield * s; c.Armor = c.MaxArmor * a;
        }
        foreach (var st in Table.Stacks) st.Dirty = true;
    }
}
