using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MultiZoneInventory
{
    // Расширяет зрение зомби-доставщика: временно подмешивает склады чужих зон
    static class HaulerVisionPatch
    {
        static readonly FieldInfo WgoListField =
            AccessTools.Field(typeof(WorldZoneData), "multiInventoryWgoDatas");

        // Зона владельца: пробуем свойство, при отсутствии — рефлексией
        static WorldZoneData GetZone(WgoData wgo)
        {
            if (wgo == null) return null;
            var prop = AccessTools.Property(typeof(WgoData), "WorldZoneData");
            if (prop != null) return prop.GetValue(wgo) as WorldZoneData;
            var field = AccessTools.Field(typeof(WgoData), "worldZoneData");
            return field != null ? field.GetValue(wgo) as WorldZoneData : null;
        }

        // Проверяем, является ли WgoData настоящим сундуком/складом, а не деревом или кустом
        private static bool IsRealStorage(WgoData wgo)
        {
            if (wgo == null || wgo.Definition == null) return false;

            // 1. Проверяем строковые ID и группы. В Graveyard Keeper склады имеют четкие wgoGroup
            string group = wgo.Definition.wgoGroup?.ToLower() ?? "";
            string id = wgo.id?.ToLower() ?? "";

            // Исключаем природные объекты, деревья, грядки, NPC и разбираемые чертежи
            if (id.Contains("tree") || id.Contains("bush") || id.Contains("stone_quarry") ||
                group.Contains("nature") || group.Contains("npc") || group.Contains("crop"))
            {
                return false;
            }

            // 2. Самый надежный маркер: игра разрешает открывать этот объект в мультиинвентарях игрока
            // и размер его инвентаря больше нуля
            return wgo.Definition.OpenInMultiInventory && wgo.Definition.inventorySize > 0;
        }

        public static void Prefix(object __instance, out List<WgoData> __state)
        {
            __state = null;

            if (__instance == null || WgoListField == null) return;

            WorldZoneData zone = __instance as WorldZoneData;
            if (zone == null)
            {
                var zwd = AccessTools.Property(__instance.GetType(), "WorldZoneData");
                zone = zwd?.GetValue(__instance) as WorldZoneData;
            }
            if (zone == null) return;

            var list = (List<WgoData>)WgoListField.GetValue(zone);
            if (list == null) return;

            // Собираем ТОЛЬКО реальные склады из нашего глобального кэша, которые находятся в чужих зонах
            var foreign = WgoInventoryCache.AllWgoDatas
                .Where(w => w != null && IsRealStorage(w) && GetZone(w) != zone && !list.Contains(w))
                .ToList();

            if (foreign.Count > 0)
            {
                __state = new List<WgoData>(list); // сохраняем ванильный оригинал списка зоны
                list.AddRange(foreign);            // подмешиваем сундуки со всего мира
            }
        }

        public static void Finalizer(object __instance, List<WgoData> __state)
        {
            if (__state == null) return;

            WorldZoneData zone = __instance as WorldZoneData;
            if (zone == null)
            {
                var zwd = AccessTools.Property(__instance.GetType(), "WorldZoneData");
                zone = zwd?.GetValue(__instance) as WorldZoneData;
            }
            if (zone == null) return;

            WgoListField.SetValue(zone, __state);  // возвращаем список зоны в исходное состояние
        }
    }
}