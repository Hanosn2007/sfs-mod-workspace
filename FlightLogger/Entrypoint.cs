using System.Collections.Generic;
using ModLoader;
using ModLoader.Helpers;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlightLogger
{
    public class Entrypoint : Mod
    {
        public static Entrypoint Main { get; private set; }

        private GameObject loggerObject;

        public override string ModNameID => "flightlogger";
        public override string DisplayName => "Flight Logger";
        public override string Author => "Codex";
        public override string MinimumGameVersionNecessary => "1.6.00.16";
        public override string ModVersion => "0.1.0";
        public override string Description => "Read-only flight telemetry CSV logger for physics analysis.";
        public override string IconLink => "";
        public override Dictionary<string, string> Dependencies => new Dictionary<string, string>();

        public override void Load()
        {
            Main = this;
            SceneHelper.OnWorldSceneLoaded += OnWorldSceneLoaded;
            SceneHelper.OnWorldSceneUnloaded += OnWorldSceneUnloaded;
            Debug.Log("[FlightLogger] Loaded");
        }

        private void OnWorldSceneLoaded(Scene scene)
        {
            StartLogger();
        }

        private void OnWorldSceneUnloaded(Scene scene)
        {
            StopLogger();
        }

        private void StartLogger()
        {
            if (loggerObject != null)
                return;

            loggerObject = new GameObject("FlightLogger");
            Object.DontDestroyOnLoad(loggerObject);
            loggerObject.AddComponent<FlightLoggerBehaviour>().Initialize(this);
        }

        private void StopLogger()
        {
            if (loggerObject == null)
                return;

            Object.Destroy(loggerObject);
            loggerObject = null;
        }
    }
}
