using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MultiZoneInventory
{
    // Расширяет зрение зомби-доставщика: временно подмешивает склады чужих зон
    // в multiInventoryWgoDatas на время выполнения патченных методов.
    /*static class HaulerVisionPatch
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

        // Подходит и для WorldZoneData (сама зона), и для ZombieWgoData (зона через WorldZoneData)
        public static void Prefix(object __instance, out List<WgoData> __state)
        {
            __state = null;

            WorldZoneData zone = __instance as WorldZoneData;
            if (zone == null)
            {
                var zwd = AccessTools.Property(__instance.GetType(), "WorldZoneData");
                zone = zwd?.GetValue(__instance) as WorldZoneData;
            }
            if (zone == null || WgoListField == null) return;

            var list = (List<WgoData>)WgoListField.GetValue(zone);
            if (list == null) return;

            var foreign = WgoInventoryCache.AllWgoDatas
                .Where(w => w != null && GetZone(w) != zone && !list.Contains(w))
                .ToList();

            if (foreign.Count > 0)
            {
                __state = new List<WgoData>(list); // сохраняем оригинал
                list.AddRange(foreign);            // подмешиваем чужие сундуки
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

            WgoListField.SetValue(zone, __state);  // вернули список как было
        }
    }*/
}