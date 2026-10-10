using System.Numerics;

namespace GrandGalactic;

public sealed class Card
{
    public int Uid;
    public required CardDef Def;
    /// <summary>Hull (hit points). Shields soak damage first, then armour, then hull.</summary>
    public float Hp, MaxHp;
    public float Shield, MaxShield, Armor, MaxArmor;
    public float ShieldRegen, ArmorRegen, HullRegen, Evasion;
    /// <summary>Components fitted to this ship or starbase.</summary>
    public readonly List<ComponentDef> Parts = new();
    /// <summary>The admiral assigned to this ship, if any (they leave the table and ride with it).</summary>
    public Card? Admiral;
    public readonly List<Gun> Guns = new();
    public bool Claimed;
    public float AttackTimer;
    public float AggroTimer;
    public float SpawnTimer;
    /// <summary>Seconds a Baby has spent growing on a City District.</summary>
    public float Grow;
    /// <summary>The fleet (stack id) a card fought from: its fleet's Admiral boosts it, and survivors regroup by it.</summary>
    public int Fleet;
    /// <summary>Seconds toward a colonised planet's next unworked yield.</summary>
    public float Passive;
    public Stack? Stack;
    public Battle? Battle;
    public override string ToString() => $"{Def.Id}#{Uid}";
}

/// <summary>One weapon on a card: its own damage, reload and damage profile (null = plain).</summary>
public sealed class Gun
{
    public ComponentDef? Profile;
    public float Damage, Cooldown, Timer;
}

public sealed class Stack
{
    public int Id;
    public Vector2 Pos;
    public readonly List<Card> Cards = new();
    public bool Dragging;
    public RecipeDef? Active;
    public Card? ActiveStation;
    public float Progress;
    public float Duration;
    public bool Dirty = true;
    /// <summary>How long the stack has been stuck overlapping something; past a moment it moves to a free spot.</summary>
    public float Jam;
    /// <summary>The build the player chose for a station whose blueprints only need resources (Construction Ship, Shipyard...).</summary>
    public RecipeDef? Order;
    /// <summary>Build orders waiting after the current one (a station queues up to 3 jobs).</summary>
    public readonly List<RecipeDef> Queue = new();
    /// <summary>Where the stack is gliding to (it moves there smoothly instead of jumping).</summary>
    public Vector2? Glide;
    /// <summary>Why the stack isn't working right now (shown above it), or null.</summary>
    public string? Wait;
    // Travel between star systems: a ship carries the stack from TravelFrom to TravelTo over TravelDur seconds.
    public Vector2 TravelFrom, TravelTo;
    public float TravelT, TravelDur;
    public bool Traveling => TravelDur > 0;

    public Card Root => Cards[0];
    public bool HasHostile => Cards.Any(c => c.Def.IsHostile);
}

public sealed class Battle
{
    public Vector2 Pos;
    /// <summary>The star system the battle is fought in; its area stays inside it.</summary>
    public StarSystem? System;
    public readonly List<Card> Players = new();
    public readonly List<Card> Hostiles = new();
}

/// <summary>The one table every card lives on.</summary>
public sealed class Board
{
    public readonly List<Stack> Stacks = new();
    public readonly List<Battle> Battles = new();

    public IEnumerable<Card> AllCards => Stacks.SelectMany(s => s.Cards).Concat(Battles.SelectMany(b => b.Players.Concat(b.Hostiles)));
}

/// <summary>One star system: a labelled area of the table. Surveying adds a new one next to the others.</summary>
public sealed class StarSystem
{
    public int Index;
    public required SystemDef Sys;
    public required string Name;
    /// <summary>What kind of system this is, shown under the name (e.g. "Black hole").</summary>
    public string Kind = "";
    public Vector2 Origin;
    public Vector2 Size;
    /// <summary>Grid slot on the table (capital at 0,0); travel time counts steps between slots.</summary>
    public (int X, int Y) Slot;
    /// <summary>Owned by the player: planets here can be colonised and worked. Limited by rules.claim_limit.</summary>
    public bool Claimed;

    public Vector2 Center => Origin + Size / 2;
    public bool Contains(Vector2 p) => p.X >= Origin.X && p.Y >= Origin.Y && p.X < Origin.X + Size.X && p.Y < Origin.Y + Size.Y;
}

public enum SimEvent { Pickup, Drop, PackOpen, Sell, Hit, Done, MoonEnd, Warning, Win, Lose }

public enum RunState { Playing, Won, Lost }

public readonly record struct Match(RecipeDef Recipe, Card Station, List<Card> Consumed, float Duration);
