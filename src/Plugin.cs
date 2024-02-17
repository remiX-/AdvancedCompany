using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using QualityCompany.Assets;
using QualityCompany.Manager.ShipTerminal;
using QualityCompany.Modules.Core;
using QualityCompany.Patch;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace QualityCompany;

[BepInPlugin(PluginMetadata.PLUGIN_GUID, PluginMetadata.PLUGIN_NAME, PluginMetadata.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    private readonly Harmony harmony = new(PluginMetadata.PLUGIN_GUID);

    internal static Plugin Instance;

    internal ManualLogSource ACLogger;

    internal PluginConfig PluginConfig;

    internal string PluginPath;

    private void Awake()
    {
        Instance = this;
        ACLogger = BepInEx.Logging.Logger.CreateLogSource(PluginMetadata.PLUGIN_NAME);

        // Asset Bundles
        PluginPath = Path.GetDirectoryName(Info.Location)!;

        // Config
        PluginConfig = new PluginConfig();
        PluginConfig.Bind(Config);

        // Plugin patch logic
        NetcodePatcher();
        Patch();
        LoadAssets();

        // Loaded
        ACLogger.LogMessage($"Plugin {PluginMetadata.PLUGIN_NAME} v{PluginMetadata.PLUGIN_VERSION} is loaded!");
    }

    private void Patch()
    {
        AdvancedTerminalRegistry.Register(Assembly.GetExecutingAssembly());
        ModuleRegistry.Register(Assembly.GetExecutingAssembly());

        harmony.PatchAll(Assembly.GetExecutingAssembly());
        harmony.PatchAll(typeof(MenuPatch));
    }

    private static void NetcodePatcher()
    {
        var types = Assembly.GetExecutingAssembly().GetTypes();
        foreach (var type in types)
        {
            var methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var method in methods)
            {
                var attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                if (attributes.Length > 0)
                {
                    method.Invoke(null, null);
                }
            }
        }
    }

    private void LoadAssets()
    {
        AssetBundleLoader.LoadModBundle(PluginPath);
    }
}