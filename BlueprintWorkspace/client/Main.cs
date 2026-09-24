using System;
using System.Collections.Generic;
using ModLoader;
using ModLoader.Helpers;
using UnityEngine;

namespace BlueprintWorkspace
{
    public sealed class Main : Mod
    {
        private GameObject holder;

        public override string ModNameID => "BlueprintWorkspace";
        public override string DisplayName => "Shared Blueprints";
        public override string Author => "Codex";
        public override string MinimumGameVersionNecessary => "1.6.00.16";
        public override string ModVersion => "0.4.0";
        public override string Description => "Private shared blueprint library for Mac and Windows PC players.";
        public override Dictionary<string, string> Dependencies { get; } = new Dictionary<string, string>();

        public override void Load()
        {
            holder = new GameObject("BlueprintWorkspace");
            UnityEngine.Object.DontDestroyOnLoad(holder);
            var ui = holder.AddComponent<WorkspaceWindow>();
            ui.Initialize(ModFolder);
            SceneHelper.OnBuildSceneLoaded += ui.OnBuildLoaded;
            SceneHelper.OnBuildSceneUnloaded += ui.OnBuildUnloaded;
            Debug.Log("[BlueprintWorkspace] Loaded");
        }
    }
}
