using HarmonyLib;
using System;
using System.Collections.Generic;
using static MultiZoneInventory.MainPlugin;

public static class WgoInventoryCache
{
    private static readonly AccessTools.FieldRef<Inventory, object> GetInventoryItem =
    AccessTools.FieldRefAccess<Inventory, object>("inventoryItem");
    private static readonly List<Inventory> _inventories = new List<Inventory>();
    private static readonly List<WgoData> _wgoDatas = new List<WgoData>();
    private static readonly Dictionary<string, int> _totals = new Dictionary<string, int>();

    private static bool _dirty = true;

    public static IEnumerable<Inventory> All => _inventories;
    public static IEnumerable<WgoData> AllWgoDatas => _wgoDatas;

    // Автоматически вызывается из Postfix патча AddWgoDataToCache
    public static void HandleWgoAdded(WgoData wgo)
    {
        if (wgo == null || wgo.Definition == null || wgo.Inventory == null) return;

        // Фильтруем контейнеры: размер > 0 и разрешено отображение в мультиинвентарях
        if (wgo.Definition.inventorySize <= 0 || !wgo.Definition.OpenInMultiInventory) return;
        if (_inventories.Contains(wgo.Inventory)) return;

        _inventories.Add(wgo.Inventory);
        _wgoDatas.Add(wgo);

        // Подписываемся на динамическое изменение предметов, чтобы инвалидировать кэш сумм
        wgo.Inventory.OnItemsAdd += OnAnyChange;
        wgo.Inventory.OnItemsRemove += OnAnyChange;

        _dirty = true;
        Log.LogMessage($"[MZI] Инвентарь добавлен в глобальный стек: {wgo.id} (UID: {wgo.UniqueId})");
    }

    // Автоматически вызывается из Postfix патча RemoveWgoDataFromCache
    public static void HandleWgoRemoved(WgoData wgo)
    {
        if (wgo == null || wgo.Inventory == null) return;

        if (_inventories.Remove(wgo.Inventory))
        {
            _wgoDatas.Remove(wgo);

            wgo.Inventory.OnItemsAdd -= OnAnyChange;
            wgo.Inventory.OnItemsRemove -= OnAnyChange;

            _dirty = true;
            Log.LogMessage($"[MZI] Инвентарь удален из глобального стека: {wgo.id}");
        }
    }

    // Очистка кэша при перезагрузке сохранения или выходе в меню
    public static void Clear()
    {
        foreach (var inv in _inventories)
        {
            if (inv != null)
            {
                inv.OnItemsAdd -= OnAnyChange;
                inv.OnItemsRemove -= OnAnyChange;
            }
        }
        _inventories.Clear();
        _wgoDatas.Clear();
        _totals.Clear();
        _dirty = true;
        Log.LogWarning("[MZI] Глобальный кэш инвентарей полностью сброшен.");
    }

    private static void OnAnyChange(List<Item> _)
    {
        _dirty = true;
    }

    public static int GetTotal(string itemId)
    {
        if (_dirty)
        {
            Recount();
        }
        return _totals.TryGetValue(itemId, out int n) ? n : 0;
    }

    private static void Recount()
    {
        _totals.Clear();
        foreach (Inventory inv in _inventories)
        {
            if (inv == null) continue;

            try
            {
                // 1. Извлекаем приватный объект inventoryItem
                object invItem = GetInventoryItem(inv);
                if (invItem == null) continue;

                // 2. Достаем из него коллекцию под-инвентарей. 
                // Как видно из вашего декомпилятора (invItem.Inventory), свойство называется "Inventory"
                var subInventories = AccessTools.Property(invItem.GetType(), "Inventory")?.GetValue(invItem) as System.Collections.IEnumerable;
                if (subInventories == null) continue;

                // 3. Обходим все вложенные инвентари в этом сундуке
                foreach (var subInv in subInventories)
                {
                    if (subInv == null) continue;

                    // В зависимости от архитектуры игры, subInv — это либо список Item, либо еще один уровень Inventory.
                    // Судя по декомпилятору: `foreach (Item item in this.inventoryItem.Inventory)` — subInv является типом Item!
                    // А у Item, как мы видим из кода сумок, есть свое свойство `.Inventory` или `.Data.Inventory`.
                    if (subInv is Item itemInSlot)
                    {
                        // Считаем сам предмет в слоте
                        ProcessItem(itemInSlot);

                        // Если это сумка внутри сундука (как Farmer's bag), заглядываем внутрь нее:
                        if (itemInSlot.IsBag)
                        {
                            var bagItems = AccessTools.Property(typeof(Item), "Inventory")?.GetValue(itemInSlot) as IEnumerable<Item>;
                            if (bagItems != null)
                            {
                                foreach (var bagItem in bagItems)
                                {
                                    ProcessItem(bagItem);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.LogError($"[MZI] Ошибка подсчета предметов в сундуке: {e.Message}\n{e.StackTrace}");
            }
        }
        _dirty = false;
    }

    // Вспомогательный метод для чистоты кода
    private static void ProcessItem(Item item)
    {
        if (item == null || string.IsNullOrEmpty(item.id) || item.IsEmpty)
            return;

        _totals.TryGetValue(item.id, out int currentCount);
        _totals[item.id] = currentCount + item.Count;
    }
}
