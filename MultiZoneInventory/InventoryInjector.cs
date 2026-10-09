using HarmonyLib;
using System.Collections.Generic;

namespace MultiZoneInventory
{
    public static class Injector
    {
        public static void Inject(MultiInventory mi)
        {
            if (mi == null || mi.inventoryList == null) return;

            // Используем HashSet для моментальной и 100% точной проверки дубликатов
            var existing = new HashSet<Inventory>();
            foreach (var inv in mi.inventoryList)
            {
                if (inv != null) existing.Add(inv);
            }

            var toAdd = new List<Inventory>();
            foreach (Inventory globalInv in WgoInventoryCache.All)
            {
                if (globalInv != null && !existing.Contains(globalInv))
                {
                    toAdd.Add(globalInv);
                }
            }

            if (toAdd.Count > 0)
            {
                // КРИТИЧЕСКИЙ ХОД: Добавляем напрямую во внутренний List<Inventory> игры.
                // Это НЕ вызовет ванильный метод mi.Add() и, следовательно, 
                // НЕ запустит нестабильную сортировку Sort() во время цикла!
                mi.inventoryList.AddRange(toAdd);

                // Вызываем скрытую ванильную сортировку ОДИН единственный раз в самом конце
                AccessTools.Method(typeof(MultiInventory), "Sort")?.Invoke(mi, null);
            }
        }
    }
}