namespace Roguelike.Core.Items;

/// <summary>Carried items. Potions of the same kind stack and take one slot.</summary>
public sealed class Inventory(int capacity)
{
    private readonly List<Item> _items = [];

    public int Capacity { get; } = capacity;
    public IReadOnlyList<Item> Items => _items;
    public bool IsFull => _items.Count >= Capacity;

    /// <summary>Whether <paramref name="item"/> fits (into an existing stack or a free slot).</summary>
    public bool CanAdd(Item item) => !IsFull || _items.Any(i => item.StacksWith(i));

    /// <summary>Adds the item; returns the item now holding it (an existing stack, or the item itself).</summary>
    public Item Add(Item item)
    {
        var stack = _items.FirstOrDefault(i => item.StacksWith(i));
        if (stack is not null)
        {
            stack.Count += item.Count;
            return stack;
        }
        if (IsFull)
            throw new InvalidOperationException("Inventory is full.");
        _items.Add(item);
        return item;
    }

    public Item? Find(int itemId) => _items.Find(i => i.Id == itemId);

    public bool Remove(Item item) => _items.Remove(item);
}

/// <summary>What an actor has equipped: one weapon, one bow, one armor.</summary>
public sealed class Equipment
{
    public Item? Weapon { get; private set; }
    public Item? Bow { get; private set; }
    public Item? Armor { get; private set; }

    public IEnumerable<Item> All => new[] { Weapon, Bow, Armor }.OfType<Item>();

    public bool IsEquipped(Item item) => item == Weapon || item == Bow || item == Armor;

    /// <summary>Puts the item in its slot; returns whatever was there before.</summary>
    public Item? Equip(Item item)
    {
        Item? previous;
        switch (item.Def.Category)
        {
            case ItemCategory.Weapon: previous = Weapon; Weapon = item; break;
            case ItemCategory.Bow: previous = Bow; Bow = item; break;
            case ItemCategory.Armor: previous = Armor; Armor = item; break;
            default: throw new ArgumentException($"{item.Def.Id} can't be equipped.");
        }
        return previous;
    }

    public void Unequip(Item item)
    {
        if (item == Weapon) Weapon = null;
        else if (item == Bow) Bow = null;
        else if (item == Armor) Armor = null;
    }
}
