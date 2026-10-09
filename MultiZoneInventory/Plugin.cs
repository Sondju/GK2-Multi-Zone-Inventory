using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MultiZoneInventory
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class MainPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.sondju.MultiZoneInventory";
        public const string PluginName = "Multi Zone Inventory";
        public const string PluginVersion = "2.0.0";

        public static MainPlugin Instance { get; private set; }
        public static ManualLogSource Log;

        // Делаем конфиги публичными свойствами, чтобы к ним легко обращались файлы патчей
        public ConfigEntry<bool> EnableMod { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;

            // 1. Объявление конфигов
            EnableMod = Config.Bind("General", "EnableMod", true, "Enable or disable the mod completely.");

            // 2. Автоматически ищет и применяет все патчи из ВСЕХ создаваемых нами файлов!
            var harmony = new Harmony(PluginGuid);

            var patchTypes = new[]
            {
                typeof(MI_Ctor_PlayerPatch),
                //typeof(ItemHasItemQuantityPatch),
                typeof(ItemGetTotalCountUiPatch),
                typeof(MziDataCtorPatch),
                typeof(ForceAutoCraftPatch),
                typeof(WgoDataCacheAddPatch),
                typeof(WgoDataCacheRemovePatch),
                typeof(GameSessionClearPatch),
            };

            foreach (var pt in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(pt).Patch();
                    Logger.LogWarning($"Патч применён: {pt.Name}");
                }
                catch (Exception e)
                {
                    Logger.LogError(
                        $"Патч {pt.Name} НЕ применился: {e.GetType().Name}: {e.Message}\n" +
                        $"Stack trace: {e.StackTrace}"
                    );
                }
            }

            // 3. Применяем фильтрацию
            PatchCraftableImplementations(harmony);

            // 4. Кросс-зоновое зрение доставщика
            /*var hvPrefix = new HarmonyMethod(AccessTools.Method(
                typeof(HaulerVisionPatch), nameof(HaulerVisionPatch.Prefix)));
            var hvFinal = new HarmonyMethod(AccessTools.Method(
                typeof(HaulerVisionPatch), nameof(HaulerVisionPatch.Finalizer)));

            harmony.Patch(AccessTools.Method(typeof(WorldZoneData), "CanDeliveryOrderBeTakenOnExecution"),
                prefix: hvPrefix, finalizer: hvFinal);

            foreach (var name in new[] {
                "CaretakerTryMoveToNearestInventoryWithRequiredItemCountToPickUp",
                "CaretakerTryPickUpFromInventory" })
            {
                harmony.Patch(AccessTools.Method(typeof(ZombieWgoData), name),
                    prefix: hvPrefix, finalizer: hvFinal);
            }*/

            // 6. Прямой старт крафта без доставщиков
            harmony.Patch(
                AccessTools.Method(typeof(ZombieWgoData), "CrafterTryPlaceOrderForCurrentCraftOrStartIt"),
                prefix: new HarmonyMethod(AccessTools.Method(
                    typeof(CrafterDirectStartPatch), nameof(CrafterDirectStartPatch.Prefix))));

            // 5. Создаём кэш инвентарей
            //WgoInventoryCache.Rebuild();
        }

        public static void PatchCraftableImplementations(Harmony harmony)
        {
            var postfix = new HarmonyMethod(AccessTools.Method(
                typeof(CraftableInjectPatch), nameof(CraftableInjectPatch.Postfix)));

            int patched = 0;
            var seen = new HashSet<MethodBase>();

            Type[] types;
            try { types = typeof(ICraftable).Assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e)
            {
                types = e.Types.Where(t => t != null).ToArray();
            }

            foreach (var type in types)
            {
                if (type.IsInterface || type.IsAbstract) continue;
                if (!typeof(ICraftable).IsAssignableFrom(type)) continue;
                //if (type.Name == "ConveyorWgoData") continue; // конвейеру только своё
                //if (type.Name == "ZombieWgoData") continue; // зомби только своё

                foreach (var m in AccessTools.GetDeclaredMethods(type))
                {
                    if (m.Name == "GetCraftableMultiInventory" && seen.Add(m))
                    {
                        try
                        {
                            harmony.Patch(m, postfix: postfix);
                            patched++;
                            Log.LogWarning($"[MZI] Patched: {type.Name}.GetCraftableMultiInventory");
                        }
                        catch (Exception e)
                        {
                            Log.LogError($"[MZI] Не удалось патчить {type.Name}: {e.Message}");
                        }
                    }
                }
            }

            Log.LogWarning($"[MZI] Всего пропатчено реализаций: {patched}");
        }

        static class CraftableInjectPatch
        {
            public static void Postfix(ref MultiInventory __result)
            {
                if (__result == null) return;
                Injector.Inject(__result);
            }
        }

        // ===== Инъекция складов в MultiInventory =====
        static class Injector
        {
            public static void Inject(MultiInventory mi)
            {
                if (mi == null || mi.inventoryList == null) return;

                foreach (Inventory inv in WgoInventoryCache.All)
                {
                    if (!mi.inventoryList.Contains(inv))
                        mi.Add(inv);
                }
            }
        }

        // ===== Вливание в инвентарь =====
        [HarmonyPatch(typeof(MultiInventory), MethodType.Constructor,
            new[] { typeof(PlayerData), typeof(bool) })]
        static class MI_Ctor_PlayerPatch
        {
            static void Postfix(MultiInventory __instance)
            {
                try { Injector.Inject(__instance); }
                catch (Exception e) { Log.LogError("[MZI] Player ctor inject: " + e); }
            }
        }

        // ===== Доступность кнопок диалога: Item.HasItemQuantityInInventory =====
        [HarmonyPatch(typeof(Item), nameof(Item.HasItemQuantityInInventory),
            new[] { typeof(string), typeof(int) })]
        static class ItemHasItemQuantityPatch
        {
            public static void Postfix(ref bool __result, string itemId, int count)
            {
                // Если ванильная проверка уже вернула true (хватает предметов), не мешаем
                if (__result)
                    return;

                // Пытаемся получить количество из твоего глобального кэша
                // Предполагается, что WgoInventoryCache.GetTotal(string id) у тебя уже реализован
                int cachedCount = WgoInventoryCache.GetTotal(itemId);

                if (cachedCount >= count)
                {
                    // Если в кэше хватает предметов, меняем результат на true
                    __result = true;
                    Log.LogMessage($"[MZI] PATCH HIT: {itemId} need={count}, cache={cachedCount} -> Unlocked!");
                }
                else
                {
                    Log.LogMessage($"[MZI] PATCH SKIP: {itemId} need={count}, cache={cachedCount} -> Still locked.");
                }
            }
        }

        // ===== Синхронизация отображения цифр на плашках (диалоги, крафт, улучшения) =====
        [HarmonyPatch(typeof(Item), nameof(Item.GetTotalCountInInventory), new[] { typeof(string), typeof(Item), typeof(bool) })]
        static class ItemGetTotalCountUiPatch
        {
            // Создаем быстрый указатель для чтения приватного поля inventoryItem из класса Inventory
            private static readonly AccessTools.FieldRef<Inventory, Item> GetPlayerInventoryItem =
                AccessTools.FieldRefAccess<Inventory, Item>("inventoryItem");

            public static void Postfix(Item __instance, ref int __result, string itemId, Item ignoredBag, bool ignoreAllBags)
            {
                if (string.IsNullOrEmpty(itemId)) return;

                // 1. Проверяем, чтоPlayerData и его инвентарь проинициализированы
                if (MainGame.PlayerData != null && MainGame.PlayerData.Inventory != null)
                {
                    // 2. Безопасно достаем приватный объект Item, который скрыт внутри инвентаря игрока
                    Item playerInventoryItem = GetPlayerInventoryItem(MainGame.PlayerData.Inventory);

                    // 3. Теперь мы сравниваем два объекта одного типа (Item и Item)
                    if (__instance == playerInventoryItem)
                    {
                        // Получаем общее количество ресурсов из нашего глобального реактивного кэша сундуков
                        int cachedGlobal = WgoInventoryCache.GetTotal(itemId);

                        // Если на глобальном складе предметов больше, подставляем это число для UI
                        if (cachedGlobal > __result)
                        {
                            __result = cachedGlobal;
                        }
                    }
                }
            }
        }

        // Патч на добавление в ванильный кэш
        [HarmonyPatch(typeof(WgoDataCache), nameof(WgoDataCache.AddWgoDataToCache))]
        static class WgoDataCacheAddPatch
        {
            static void Postfix(WgoData wgoData)
            {
                try
                {
                    WgoInventoryCache.HandleWgoAdded(wgoData);
                }
                catch (Exception e)
                {
                    MainPlugin.Log.LogError($"[MZI] Ошибка обработки AddWgoDataToCache: {e.Message}");
                }
            }
        }

        // Патч на удаление из ванильного кэша
        [HarmonyPatch(typeof(WgoDataCache), nameof(WgoDataCache.RemoveWgoDataFromCache))]
        static class WgoDataCacheRemovePatch
        {
            static void Postfix(WgoData wgoData)
            {
                try
                {
                    WgoInventoryCache.HandleWgoRemoved(wgoData);
                }
                catch (Exception e)
                {
                    MainPlugin.Log.LogError($"[MZI] Ошибка обработки RemoveWgoDataFromCache: {e.Message}");
                }
            }
        }

        // Патч очистки при выходе в главное меню или загрузке
        [HarmonyPatch(typeof(MainGame), "ResetAllGameData")]
        static class GameSessionClearPatch
        {
            static void Postfix()
            {
                try
                {
                    WgoInventoryCache.Clear();
                    MainPlugin.Log.LogWarning("[MZI] Метод ResetAllGameData выполнен. Глобальный кэш инвентарей успешно очищен.");
                }
                catch (Exception e)
                {
                    MainPlugin.Log.LogError($"[MZI] Ошибка очистки кэша в ResetAllGameData Postfix: {e.Message}");
                }
            }
        }

        // Патч на конструктор UIMultiInventoryWindowData
        [HarmonyPatch(typeof(UIMultiInventoryWindowData), MethodType.Constructor,
            new[] { typeof(PlayerData), typeof(Action<UIItemCell>), typeof(Func<Item, bool>),
            typeof(bool), typeof(string), typeof(Func<Item, string>) })]
        static class MziDataCtorPatch
        {
            static void Postfix(UIMultiInventoryWindowData __instance)
            {
                try
                {
                    var mi = __instance.MultiInventory; // публичное свойство — берём напрямую
                    if (mi == null) return;

                    Injector.Inject(mi);
                }
                catch (Exception e)
                {
                    Log.LogError("[MZI] DataCtor inject: " + e.Message);
                }
            }
        }

        // Автокрафт для всего
        [HarmonyPatch(typeof(CraftComponent), "IsAutoCraftable", MethodType.Getter)]
        public static class ForceAutoCraftPatch
        {
            // --- НАШ БЕЛЫЙ СПИСОК ВЕРСТАКОВ ---
            // Автокрафт сработает ТОЛЬКО на объектах, чьи ID или wgoGroup находятся здесь.
            // Сюда вы будете вносить ID производственных станций из Unity Explorer.
            private static readonly HashSet<string> AllowedAutoCrafterIds = new HashSet<string>
            {
                "saw_bench",
                "furnace_1",
                "furnace_2"
            };

            public static bool Prefix(CraftComponent __instance, ref bool __result)
            {
                if (__instance == null || __instance.CraftableObject == null) return true;

                WgoData wgoData = null;

                try
                {
                    // Используем цепочку связей игры: CraftComponent -> CraftableObject -> Worker -> AttachedWgoData
                    var zombieWorker = __instance.CraftableObject.CraftableAttachedWorker as ZombieWgoData;
                    if (zombieWorker != null)
                    {
                        wgoData = zombieWorker.AttachedWgoData;
                    }
                    else
                    {
                        // Приводим к MemberInfo, чтобы оператор ?? сработал корректно
                        var objDataField = (MemberInfo)AccessTools.Field(__instance.CraftableObject.GetType(), "data")
                                           ?? (MemberInfo)AccessTools.Field(__instance.CraftableObject.GetType(), "wgoData")
                                           ?? (MemberInfo)AccessTools.Property(__instance.CraftableObject.GetType(), "WgoData");

                        if (objDataField != null)
                        {
                            // Извлекаем значение в зависимости от того, поле это или свойство
                            wgoData = objDataField switch
                            {
                                FieldInfo f => f.GetValue(__instance.CraftableObject) as WgoData,
                                PropertyInfo p => p.GetValue(__instance.CraftableObject) as WgoData,
                                _ => null
                            };
                        }
                    }
                }
                catch (Exception e)
                {
                    Log.LogError($"[MZI] Не удалось извлечь WgoData верстака: {e.Message}");
                }

                // Если не смогли определить верстак — отдаем управление ванили
                if (wgoData == null) return true;

                // Проверяем, входит ли верстак в наш белый список
                bool isAllowed = AllowedAutoCrafterIds.Contains(wgoData.id) ||
                                 (wgoData.Definition != null && AllowedAutoCrafterIds.Contains(wgoData.Definition.wgoGroup));

                if (isAllowed)
                {
                    var current = __instance.CurrentCraftElement;
                    if (current == null || current.Def == null) return true;

                    // Запрещаем автокрафт для системных вещей (заправка топливом)
                    if (current.Def.isFuelCraft)
                    {
                        return true;
                    }

                    // Включаем автоматику!
                    __result = true;
                    return false; // Блокируем оригинал
                }

                // Все остальные объекты (включая разбираемые сундуки) работают в ручном режиме
                return true;
            }
        }
    }
}