using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using ModLoader;
using ModLoader.Helpers;
using SFS.Builds;
using SFS.IO;
using UnityEngine;

namespace BuildTools
{
    public class Main : Mod
    {
        public static Main Instance { get; private set; }
        Harmony patcher;

        public Main()
        {
            Instance = this;
        }

        public override string ModNameID => "buildtools";
        public override string DisplayName => "Build Tools";
        public override string Author => "Codex / CucumberSpace / Astro The Rabbit / StarMods";
        public override string MinimumGameVersionNecessary => "1.6.00.16";
        public override string ModVersion => "0.1-macos";
        public override string Description => "Unified build editor with part variables, JSON editing, and build settings.";
        public override Dictionary<string, string> Dependencies { get; } = new() { { "UITools", "1.1.6" } };
        public override Action LoadKeybindings => BuildSettings.BS_Keybindings.LoadKeybindings;

        public override void Early_Load()
        {
            patcher = new Harmony(ModNameID);
            PatchAllSafely();
        }

        public override void Load()
        {
            BuildSettings.Config.MigrateLegacySettings();
            PartEditor.Config.Setup();
            PartEditor.ConfigGUI.Setup();
            PartText.Settings.Init();
            BuildSettings.Config.Setup();

            SceneHelper.OnBuildSceneLoaded += OnBuildLoaded;
            SceneHelper.OnBuildSceneUnloaded += PartText.UI.OnBuildUnloaded;
            SceneHelper.OnBuildSceneUnloaded += TabbedPanel.OnBuildUnloaded;
        }

        void OnBuildLoaded()
        {
            PartEditor.GUI.Setup();
            BuildManager.main.selector.onSelectedChange += PartEditor.GUI.OnSelectionChanged;
            PartEditor.GUI.OnSelectionChanged();

            if (PartText.Settings.settings.windowEnabled)
                PartText.UI.OnBuildLoaded();

            BuildSettings.GUI.Setup();
            BuildSettings.SkinUnlocker.Initialize();
            TabbedPanel.Initialize(PartEditor.GUI.Window, BuildSettings.GUI.Window,
                PartText.Settings.settings.windowEnabled ? PartText.UI.window : null);
        }

        void PatchAllSafely()
        {
            foreach (Type type in typeof(Main).Assembly.GetTypes())
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0)
                    continue;
                try
                {
                    patcher.CreateClassProcessor(type).Patch();
                    if (type == typeof(BuildSettings.CustomRotation))
                        BuildSettings.CustomRotation.PatchActive = true;
                }
                catch (Exception ex)
                {
                    Exception root = ex.GetBaseException();
                    Debug.LogWarning($"BuildTools skipped patch {type.FullName}: {ex.GetType().Name}: {ex.Message}; root: {root.GetType().Name}: {root.Message}");
                }
            }
        }
    }
}

namespace PartEditor
{
    public static class Main
    {
        public static BuildTools.Main main => BuildTools.Main.Instance;
    }
}

namespace PartText
{
    public static class Entrypoint
    {
        public static BuildTools.Main Main => BuildTools.Main.Instance;
    }
}

namespace BuildSettings
{
    public static class Main
    {
        public static BuildTools.Main main => BuildTools.Main.Instance;
        public static FolderPath modFolder => new(BuildTools.Main.Instance.ModFolder);
    }
}
