using BepInEx;
using GK2.Framework;
using MultiZoneInventory;

namespace MultiZoneInventoryFramework
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency(MainPlugin.PluginGuid, BepInDependency.DependencyFlags.HardDependency)]
    [BepInDependency("ru.superman4eg.gk2.framework", BepInDependency.DependencyFlags.HardDependency)]
    public sealed class FrameworkBridgePlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "com.sondju.MultiZoneInventory.framework";
        public const string PluginName = "Multi Zone Inventory";
        public const string PluginVersion = "2.0.0";

        private void Awake()
        {
            MainPlugin main = MainPlugin.Instance;
            if (main == null)
            {
                MainPlugin.Log.LogError("Main mod instance is unavailable.");
                return;
            }

            FrameworkApi.RegisterMod(new FrameworkBridge(main), main.Config);
            MainPlugin.Log.LogMessage("Multi Zone Inventory - Framework Integration Bridge loaded successfully with Native Autolocalization!");
        }

        private sealed class FrameworkBridge : Gk2ModBase
        {
            private readonly MainPlugin main;

            private readonly Gk2ModMetadata metadata = new Gk2ModMetadata(
                PluginGuid,
                PluginName,
                "sondju",
                PluginVersion,
                "Optional GK2 Mod Framework integration for Multi Zone Inventory.",
                supportsRuntimeToggle: true,
                requiresKnownBuild: false,
                frameworkManagesEnabledState: false);

            internal FrameworkBridge(MainPlugin main) { this.main = main; }
            public override Gk2ModMetadata Metadata => metadata;
            public override System.Collections.Generic.IReadOnlyList<Gk2ModDependency> Dependencies => null;

            public override void OnRegister(Gk2ModContext context)
            {
                // 1. General
                context.Settings.AddToggle(
                    "General",
                    "EnableMod",
                    true,
                    "Enable Mod",
                    "Enable or disable the mod completely.");

                // Информационная строка статуса
                context.Settings.AddReadOnly(
                    "Status",
                    "Integration",
                    "Framework Integration",
                    "Shows whether the optional bridge is active.",
                    () => main != null ? "Active" : "Unavailable");
            }

            public override void OnEnable() { }
            public override void OnDisable() { }
            public override void OnGameStarted() { }
            public override void OnReturnedToMainMenu() { }
        }
    }
}
