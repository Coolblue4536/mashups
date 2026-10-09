using System.Numerics;

namespace GrandGalactic;

public sealed class Card
{
    public int Uid;
    public required CardDef Def;
    public int Hp;
    public bool Claimed;
    public float AttackTimer;
    public float AggroTimer;
    public float SpawnTimer;
    public Stack? Stack;
    public Battle? Battle;
    public override string ToString() => $"{Def.Id}#{Uid}";
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

    public Card Root => Cards[0];
    public bool HasHostile => Cards.Any(c => c.Def.IsHostile);
}

public sealed class Battle
{
    public required Board Board;
    public Vector2 Pos;
    public readonly List<Card> Players = new();
    public readonly List<Card> Hostiles = new();
}

public sealed class Board
{
    public int Index;
    public required SystemDef Sys;
    public required string Name;
    public readonly List<Stack> Stacks = new();
    public readonly List<Battle> Battles = new();

    public IEnumerable<Card> AllCards => Stacks.SelectMany(s => s.Cards).Concat(Battles.SelectMany(b => b.Players.Concat(b.Hostiles)));
}

public enum SimEvent { Pickup, Drop, PackOpen, Sell, Hit, Done, MoonEnd, Warning, Win, Lose }

public enum RunState { Playing, Won, Lost }

public readonly record struct Match(RecipeDef Recipe, Card Station, List<Card> Consumed, float Duration);
