using HarmonyLib;
using LazyBearTechnology;
using System;
using System.Collections;

namespace MultiZoneInventory
{
    public static class FilterContextManager
    {
        // Вспомогательный метод для БЕЗОПАСНОЙ проверки активности окна по его строковому имени класса
        private static bool IsWindowActiveSafe(string windowTypeName)
        {
            try
            {
                var cacheField = AccessTools.Field(typeof(LazyUI), "windowsCache");
                if (cacheField != null && cacheField.GetValue(null) is IDictionary cache)
                {
                    foreach (var key in cache.Keys)
                    {
                        if (key is Type t && t.Name == windowTypeName)
                        {
                            // Если окно найдено в кэше Unity UI, проверяем, активно ли оно на сцене
                            if (cache[key] is LazyWidgetBase widget && widget != null && widget.gameObject != null)
                            {
                                return widget.gameObject.activeInHierarchy;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        // ВЕЛИКОЛЕПНЫЙ МЕТОД ИСКЛЮЧЕНИЯ
        public static bool IsSelectionContextActive()
        {
            // Проверяем, что синглтон плагина инициализирован
            if (MainPlugin.Instance == null) return false;

            // 1. Проверяем, открыто ли сейчас базовое окно инвентаря игрока (Tab / I)
            if (IsWindowActiveSafe("CharacterWindow"))
            {
                return false; // Пропускаем
            }

            // 2. Проверяем, открыто ли сейчас базовое окно одиночного сундука
            if (IsWindowActiveSafe("UIBaseChestWindow") || IsWindowActiveSafe("UIChestWindow"))
            {
                return false; // Пропускаем
            }

            // 3. Проверяем, открыто ли сейчас окно строительства
            if (IsWindowActiveSafe("UIBuildingWidget") || IsWindowActiveSafe("UIBuildingWindow"))
            {
                return false; // Пропускаем
            }

            // ВО ВСЕХ ОСТАЛЬНЫХ СЛУЧАЯХ ПРИМЕНЯЕМ
            return true;
        }
    }
}