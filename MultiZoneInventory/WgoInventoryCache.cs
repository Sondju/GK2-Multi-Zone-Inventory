using System.Collections.Generic;
using static MultiZoneInventory.MainPlugin;

public static class WgoInventoryCache
{
    private static readonly List<Inventory> _inventories = new List<Inventory>();
    private static readonly List<WgoData> _wgoDatas = new List<WgoData>();
    private static readonly Dictionary<string, int> _totals = new Dictionary<string, int>();
    private static bool _built;
    private static bool _dirty = true;

    public static IEnumerable<Inventory> All { get { EnsureBuilt(); return _inventories; } }
    public static IEnumerable<WgoData> AllWgoDatas { get { EnsureBuilt(); return _wgoDatas; } }

    public static bool IsReady
    {
        get
        {
            EnsureBuilt(); // соберёт, если ещё не собирали или помечен _dirty
            return _built && !_dirty;
        }
    }

    public static void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        Rebuild();
    }

    public static void Rebuild()
    {
        var save = MainGame.Instance != null ? MainGame.Instance.GameSave : null;
        if (save == null || save.worldData == null) { _built = false; return; }

        foreach (Inventory inv in _inventories) Unsubscribe(inv);
        _inventories.Clear();
        _wgoDatas.Clear();

        foreach (GameSceneData scene in save.worldData.gameSceneDataList)
        {
            if (scene == null) continue;
            foreach (WgoData wgo in scene.wgoDataList)
                Register(wgo);
        }

        _dirty = true;
        Log.LogWarning($"[MZI] Cached {_inventories.Count} storage inventories");
    }

    private static void Register(WgoData wgo)
    {
        if (wgo == null || wgo.Definition == null || wgo.Inventory == null) return;
        if (wgo.Definition.inventorySize == 0 || !wgo.Definition.OpenInMultiInventory) return;
        if (_inventories.Contains(wgo.Inventory)) return;

        _inventories.Add(wgo.Inventory);
        _wgoDatas.Add(wgo);
        wgo.Inventory.OnItemsAdd += OnAnyChange;
        wgo.Inventory.OnItemsRemove += OnAnyChange;
    }

    private static void Unsubscribe(Inventory inv)
    {
        inv.OnItemsAdd -= OnAnyChange;
        inv.OnItemsRemove -= OnAnyChange;
    }

    private static void OnAnyChange(List<Item> _) { _dirty = true; }

    // Вызывается из postfix на WorldData.AddWgoData — ловим построенные в игре склады
    public static void RegisterWgo(WgoData wgo)
    {
        EnsureBuilt();
        Register(wgo);
        _dirty = true;
    }

    public static int GetTotal(string itemId)
    {
        EnsureBuilt();
        if (_dirty) Recount();
        return _totals.TryGetValue(itemId, out int n) ? n : 0;
    }

    private static void Recount()
    {
        _totals.Clear();
        foreach (Inventory inv in _inventories)
        {
            foreach (Item item in inv.Data.Inventory) // <-- ОПАСНО: свойство Inventory может быть списком стеков, а не предметов
            {
                if (item == null || string.IsNullOrEmpty(item.id)) continue;
                _totals.TryGetValue(item.id, out int c);
                _totals[item.id] = c + item.Count;
            }
        }
        _dirty = false;
    }
}