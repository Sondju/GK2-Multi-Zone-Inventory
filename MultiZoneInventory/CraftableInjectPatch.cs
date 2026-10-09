using HarmonyLib;
using System;
using System.Collections.Generic;

namespace MultiZoneInventory
{
    static class CraftableInjectPatch
    {
        public static void Postfix(WgoData __instance, ref MultiInventory __result)
        {
            // Если игра вернула пустой список или это зомби/конвейер — не трогаем
            if (__result == null || __result.inventoryList == null) return;
            if (__instance is ZombieWgoData || __instance is ConveyorWgoData) return;

            try
            {
                // 1. Создаем быстрый хэшсет сундуков, которые игра УЖЕ сама добавила из текущей зоны
                var existingInventories = new HashSet<Inventory>();
                foreach (var inv in __result.inventoryList)
                {
                    if (inv != null)
                    {
                        existingInventories.Add(inv);
                    }
                }

                // 2. Берем сундуки из нашего глобального кэша
                foreach (Inventory globalInv in WgoInventoryCache.All)
                {
                    if (globalInv == null) continue;

                    // КРИТИЧЕСКИЙ МОМЕНТ: Внедряем сундук ТОЛЬКО если его еще нет в списке верстака!
                    // Это полностью исключит дублирование сундуков текущей зоны.
                    if (!existingInventories.Contains(globalInv))
                    {
                        __result.inventoryList.Add(globalInv);
                    }
                }

                // 3. Вызываем скрытый ванильный метод сортировки, чтобы игра упорядочила итоговый список
                AccessTools.Method(typeof(MultiInventory), "Sort")?.Invoke(__result, null);
            }
            catch (Exception e)
            {
                MainPlugin.Log.LogError($"[MZI] Ошибка фильтрации инъекции WgoData: {e.Message}");
            }
        }
    }
}