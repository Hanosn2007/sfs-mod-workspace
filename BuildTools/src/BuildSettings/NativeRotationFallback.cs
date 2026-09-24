using System;
using System.Collections.Generic;
using System.Reflection;
using SFS.Builds;
using SFS.Input;
using UnityEngine;

namespace BuildSettings
{
    // Unity 6 on macOS can reject Harmony's BuildMenus.Rotate patch. Replace only
    // the game's two native rotation callbacks when that patch did not install.
    public sealed class NativeRotationFallback : MonoBehaviour
    {
        static readonly FieldInfo OnKeyDownActions = typeof(KeysNode).GetField(
            "onKeyDownActions", BindingFlags.Instance | BindingFlags.NonPublic);

        readonly List<Action>[] actionLists = new List<Action>[2];
        readonly Action[] originalActions = new Action[2];
        readonly Action[] replacementActions = new Action[2];
        bool installed;
        int attempts;

        void Update()
        {
            if (installed || CustomRotation.PatchActive)
                return;

            if (TryInstall())
                return;

            if (++attempts == 120)
                Debug.LogWarning("BuildTools rotation fallback could not find the game's Q/E callbacks.");
        }

        bool TryInstall()
        {
            BuildMenus menus = BuildManager.main?.buildMenus;
            KeysNode keysNode = BuildManager.main?.build_Input?.keysNode;
            if (menus == null || keysNode == null || OnKeyDownActions == null)
                return false;

            var actionsByKey = OnKeyDownActions.GetValue(keysNode) as Dictionary<I_Key, List<Action>>;
            if (actionsByKey == null || KeybindingsPC.keys?.Rotate_Part == null ||
                KeybindingsPC.keys.Rotate_Part.Length < 2)
                return false;

            int[] indices = new int[2];
            for (int i = 0; i < 2; i++)
            {
                if (!actionsByKey.TryGetValue(KeybindingsPC.keys.Rotate_Part[i], out List<Action> actions))
                    return false;
                int index = actions.FindIndex(action => ReferenceEquals(action.Target, menus));
                if (index < 0)
                    return false;
                actionLists[i] = actions;
                indices[i] = index;
                originalActions[i] = actions[index];
            }

            for (int i = 0; i < 2; i++)
            {
                bool negative = i == 1;
                replacementActions[i] = () =>
                {
                    if (!PartText.UI.IsEditingText)
                        menus.Rotate(GUI.GetRotationValue(!GUI.invertKeys, negative));
                };
                actionLists[i][indices[i]] = replacementActions[i];
            }

            installed = true;
            Debug.Log("BuildTools rotation fallback bound native Q/E to Rotation Degrees.");
            return true;
        }

        void OnDestroy()
        {
            if (!installed)
                return;
            for (int i = 0; i < 2; i++)
            {
                int index = actionLists[i]?.IndexOf(replacementActions[i]) ?? -1;
                if (index >= 0)
                    actionLists[i][index] = originalActions[i];
            }
        }
    }
}
