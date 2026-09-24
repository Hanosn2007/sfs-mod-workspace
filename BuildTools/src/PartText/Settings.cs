using System;
using SFS.IO;
using UITools;
using UnityEngine;

// ReSharper disable ConvertToConstant.Global
// ReSharper disable FieldCanBeMadeReadOnly.Global

namespace PartText
{
    public class Settings : ModSettings<SettingsData>
    {
        public static Settings Main { get; private set; }
        public static Action Save { get; private set; }
        protected override FilePath SettingsFile => new FolderPath(Entrypoint.Main.ModFolder).ExtendToFile("Settings.txt");
        

        public static void Init()
        {
            Main = new Settings();
            Main.Initialize();
            if (settings.textSizeVersion == 0)
            {
                if (settings.textSize == 20)
                    settings.textSize = 28;
                settings.textSizeVersion = 1;
                Save?.Invoke();
            }
        }
        
        protected override void RegisterOnVariableChange(Action onChange)
        {
            Application.quitting += Save = onChange;
        }
    }

    public class SettingsData {
        public bool disableDoubleClickSelect = false;
        public bool fixMirroringRotatedParts = true;
        public bool windowEnabled = true;
        public Vector2 windowSize = new Vector2(400, 600);
        public bool windowMinimized = false;
        public float windowOpacity = 1f;
        public int textSize = 28;
        public int textSizeVersion;
    }
}
