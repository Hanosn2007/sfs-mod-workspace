using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BlueprintWorkspace
{
    internal sealed class LocalBlueprint
    {
        internal string Name;
        internal string Folder;
    }

    internal static class BlueprintFiles
    {
        internal static string Root
        {
            get
            {
                // Unity's dataPath differs between Mac game builds. Walk toward the
                // game root and use the first actual blueprint directory.
                DirectoryInfo current = new DirectoryInfo(Application.dataPath);
                for (int depth = 0; current != null && depth < 7; depth++, current = current.Parent)
                {
                    string candidate = Path.Combine(current.FullName, "Saving", "Blueprints");
                    if (Directory.Exists(candidate)) return candidate;
                }
                return Path.Combine(Application.persistentDataPath, "Saving", "Blueprints");
            }
        }

        internal static List<LocalBlueprint> List()
        {
            if (!Directory.Exists(Root)) return new List<LocalBlueprint>();
            return Directory.GetDirectories(Root)
                .Where(folder => File.Exists(Path.Combine(folder, "Blueprint.txt")) &&
                                 File.Exists(Path.Combine(folder, "Version.txt")))
                .Select(folder => new LocalBlueprint { Name = Path.GetFileName(folder), Folder = folder })
                .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        internal static PublishRequest Read(LocalBlueprint item)
        {
            string blueprint = File.ReadAllText(Path.Combine(item.Folder, "Blueprint.txt"));
            string version = File.ReadAllText(Path.Combine(item.Folder, "Version.txt"));
            Validate(blueprint, version);
            return new PublishRequest { Name = item.Name, Blueprint = blueprint, Version = version };
        }

        internal static string Import(FetchReply item, string modFolder)
        {
            Validate(item.Blueprint, item.Version);
            if (!Directory.Exists(Root)) throw new IOException("Game blueprint directory does not exist yet. Save one blueprint in game first.");
            string safeName = SafeName(item.Name);
            string target = Path.Combine(Root, safeName);
            for (int number = 2; Directory.Exists(target); number++)
                target = Path.Combine(Root, safeName + " (shared " + number + ")");

            string stagingRoot = Path.Combine(modFolder, "ImportStaging");
            Directory.CreateDirectory(stagingRoot);
            string staging = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, "Blueprint.txt"), item.Blueprint);
            File.WriteAllText(Path.Combine(staging, "Version.txt"), item.Version);
            Directory.Move(staging, target);
            return Path.GetFileName(target);
        }

        private static void Validate(string blueprint, string version)
        {
            if (string.IsNullOrEmpty(blueprint) || blueprint.Length > 2 * 1024 * 1024 ||
                string.IsNullOrEmpty(version) || version.Length > 80)
                throw new InvalidDataException("Blueprint or version is missing or too large.");
            JObject data;
            try { data = JObject.Parse(blueprint); }
            catch (Exception e) { throw new InvalidDataException("Blueprint JSON is invalid.", e); }
            if (!(data["parts"] is JArray) || !(data["stages"] is JArray))
                throw new InvalidDataException("This is not a Spaceflight Simulator blueprint.");
        }

        private static string SafeName(string name)
        {
            string result = new string((name ?? "").Select(c =>
                c == '/' || c == '\\' || Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c).ToArray());
            result = result.Trim().TrimEnd('.', ' ');
            if (result.Length > 80) result = result.Substring(0, 80).TrimEnd('.', ' ');
            return result == "" ? "Shared blueprint" : result;
        }
    }
}
