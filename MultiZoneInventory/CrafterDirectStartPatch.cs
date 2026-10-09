using System.Reflection;
using HarmonyLib;
using MultiZoneInventory;

public static class CrafterDirectStartPatch
{
    static readonly FieldInfo OrderedCraftIdField =
        AccessTools.Field(typeof(ZombieWgoData), "crafterOrderedCraftId");

    // --- приватные члены через рефлексию (кэшируем один раз) ---
    static readonly MethodInfo mCanUseTool =
        AccessTools.Method(typeof(ZombieWgoData), "CrafterCanUseTool");
    static readonly MethodInfo mEnoughMastery =
        AccessTools.Method(typeof(ZombieWgoData), "CrafterIsEnoughMastery");
    static readonly MethodInfo mDeliversResult =
        AccessTools.Method(typeof(ZombieWgoData), "CrafterDeliversResultBeforeNextCraft");
    static readonly MethodInfo mStopActivity =
        AccessTools.Method(typeof(ZombieWgoData), "CrafterStopCraftActivity");
    static readonly MethodInfo mStartActivity =
        AccessTools.Method(typeof(ZombieWgoData), "CrafterStartCraftActivity");

    // ZombieCraftActivity может быть свойством или полем — ловим оба варианта
    static readonly MemberInfo zombieCraftActivity =
        (MemberInfo)AccessTools.Property(typeof(ZombieWgoData), "ZombieCraftActivity")
        ?? AccessTools.Field(typeof(ZombieWgoData), "ZombieCraftActivity");

    static object GetActivity(ZombieWgoData z) => zombieCraftActivity switch
    {
        PropertyInfo p => p.GetValue(z),
        FieldInfo f => f.GetValue(z),
        _ => null
    };

    public static bool Prefix(ZombieWgoData __instance)
    {
        var attached = __instance.AttachedWgoData;
        var component = attached?.CraftComponent;
        if (component == null) return true;

        var current = component.CurrentCraftElement;
        if (current == null || current.IsStarted) return true;   // как в оригинале
        if (__instance.CrafterOrders.Count > 0) return true;     // заказ уже есть — не мешаем

        if (!(bool)mCanUseTool.Invoke(__instance, new object[] { attached, current.Def }))
            return true;
        if (!(bool)mEnoughMastery.Invoke(__instance, new object[] { attached }))
            return true;

        // Проверка ресурсов — тем же путём, что и автокрафт куба:
        // multiInventory = null → CanStartCraft сам подставит GetCraftableMultiInventory
        var status = current.CanStartCraft(attached, null);
        MainPlugin.Log.LogMessage($"[MZI] DirectStart {attached.UniqueId}: CanStartCraft = {status}");
        if (status != CraftStatus.OK) return true; // ресурсов нет даже глобально — пусть работает ваниль

        // Всё есть в мире — стартуем напрямую, без DeliveryOrder
        if (__instance.CrafterCurrentOrder == null)
        {
            if ((bool)mDeliversResult.Invoke(__instance, null) && GetActivity(__instance) != null)
                mStopActivity.Invoke(__instance, null);

            if (GetActivity(__instance) == null)
            {
                mStartActivity.Invoke(__instance, new object[] { true });
                OrderedCraftIdField?.SetValue(__instance, string.Empty);
            }
        }
        return false; // оригинал не выполняем — заказ на хаулера не создаётся
    }
}
