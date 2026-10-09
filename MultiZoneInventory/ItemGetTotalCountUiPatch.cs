using HarmonyLib;

namespace MultiZoneInventory
{
    [HarmonyPatch(typeof(Item), nameof(Item.GetTotalCountInInventory), new[] { typeof(string), typeof(Item), typeof(bool) })]
    static class ItemGetTotalCountUiPatch
    {
        private static readonly AccessTools.FieldRef<Inventory, Item> GetPlayerInventoryItem =
            AccessTools.FieldRefAccess<Inventory, Item>("inventoryItem");

        public static void Postfix(Item __instance, ref int __result, string itemId, Item ignoredBag, bool ignoreAllBags)
        {
            if (string.IsNullOrEmpty(itemId)) return;

            // Если открыто любое строительное или производственное окно — выходим!
            // Проверяем строительный виджет (где двоило 420 вместо 410) и стандартные меню крафта
            if (!FilterContextManager.IsSelectionContextActive()) return;

            // 3. Этот чистый блок сработает исключительно в окнах реплик диалогов с NPC
            if (MainGame.PlayerData != null && MainGame.PlayerData.Inventory != null)
            {
                Item playerInventoryItem = GetPlayerInventoryItem(MainGame.PlayerData.Inventory);

                // Если игра опрашивает инвентарь в карманах главного героя внутри диалога
                if (__instance == playerInventoryItem)
                {
                    int cachedGlobal = WgoInventoryCache.GetTotal(itemId);
                    if (cachedGlobal > __result)
                    {
                        __result = cachedGlobal;
                    }
                }
            }
        }
    }
}
