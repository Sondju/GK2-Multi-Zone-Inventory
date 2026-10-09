using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using LazyBearTechnology;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
                typeof(ItemGetTotalCountUiPatch),
                typeof(MziDataCtorPatch),
                typeof(WgoDataCacheAddPatch),
                typeof(WgoDataCacheRemovePatch),
                typeof(GameSessionClearPatch),
            };

            foreach (var pt in patchTypes)
            {
                try
                {
                    harmony.CreateClassProcessor(pt).Patch();
                    Logger.LogMessage($"{pt.Name} correct patched");
                }
                catch (Exception e)
                {
                    Logger.LogError(
                        $"{pt.Name} NOT patched: {e.GetType().Name}: {e.Message}\n" +
                        $"Stack trace: {e.StackTrace}"
                    );
                }
            }

            // 3. Применяем фильтрацию
            PatchCraftableImplementations(harmony);

            // 4. Кросс-зоновое зрение доставщика
            var hvPrefix = new HarmonyMethod(AccessTools.Method(
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
            }
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

                // --- СТРОЖАЙШИЙ ФИЛЬТР ДЛЯ ЗАЩИТЫ ОТ ДВОЙНОГО СПИСАНИЯ ---
                // Разрешаем патчить ТОЛЬКО базовый класс WgoData (где живут все верстаки).
                // Зомби и конвейеры ОБЯЗАНЫ работать по своей ванильной логике!
                if (type.Name == "ConveyorWgoData") continue; // Конвейеру — только своё топливо
                if (type.Name == "ZombieWgoData") continue;   // Зомби — только его личный инвентарь

                foreach (var m in AccessTools.GetDeclaredMethods(type))
                {
                    if (m.Name == "GetCraftableMultiInventory" && seen.Add(m))
                    {
                        try
                        {
                            harmony.Patch(m, postfix: postfix);
                            patched++;
                            Log.LogWarning($"[MZI] Успешно пропатчен верстак: {type.Name}.GetCraftableMultiInventory");
                        }
                        catch (Exception e)
                        {
                            Log.LogError($"[MZI] Не удалось патчить {type.Name}: {e.Message}");
                        }
                    }
                }
            }

            Log.LogWarning($"[MZI] Всего успешно пропатчено верстаков: {patched}");
        }

        // ===== Вливание в инвентарь игрока =====
        [HarmonyPatch(typeof(MultiInventory), MethodType.Constructor, new[] { typeof(PlayerData), typeof(bool) })]
        static class MI_Ctor_PlayerPatch
        {
            static void Postfix(MultiInventory __instance)
            {
                try { Injector.Inject(__instance); }
                catch (Exception e) { Log.LogError("[MZI] Player ctor inject: " + e); }
            }
        }

        // ===== Патч на конструктор данных окон MultiInventory =====
        [HarmonyPatch(typeof(UIMultiInventoryWindowData), MethodType.Constructor,
            new[] { typeof(PlayerData), typeof(Action<UIItemCell>), typeof(Func<Item, bool>),
            typeof(bool), typeof(string), typeof(Func<Item, string>) })]
        static class MziDataCtorPatch
        {
            static void Postfix(UIMultiInventoryWindowData __instance)
            {
                try
                {
                    var mi = __instance.MultiInventory;
                    if (mi != null) Injector.Inject(mi);
                }
                catch (Exception e) { Log.LogError("[MZI] DataCtor inject: " + e.Message); }
            }
        }

        // Патчи кэша WgoDataCache
        [HarmonyPatch(typeof(WgoDataCache), nameof(WgoDataCache.AddWgoDataToCache))]
        static class WgoDataCacheAddPatch { static void Postfix(WgoData wgoData) => WgoInventoryCache.HandleWgoAdded(wgoData); }

        [HarmonyPatch(typeof(WgoDataCache), nameof(WgoDataCache.RemoveWgoDataFromCache))]
        static class WgoDataCacheRemovePatch { static void Postfix(WgoData wgoData) => WgoInventoryCache.HandleWgoRemoved(wgoData); }

        [HarmonyPatch(typeof(MainGame), "ResetAllGameData")]
        static class GameSessionClearPatch { static void Postfix() => WgoInventoryCache.Clear(); }
    }
}