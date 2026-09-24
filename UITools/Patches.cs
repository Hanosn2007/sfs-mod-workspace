using System;
using HarmonyLib;
using SFS.UI;
using UnityEngine;
using Object = UnityEngine.Object;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Local
// ReSharper disable InconsistentNaming

namespace UITools
{
    static class GameHook
    {
        internal static readonly GameObject_Local settingsMenu = new() { Value = null };
        internal static event Action OnSettingsMenuClosed;
        internal static event Action OnSettingsMenuOpened;
        static bool polling;

        internal static void StartPolling()
        {
            if (polling)
                return;
            polling = true;
            GameObject holder = new("UITools Menu Poller");
            Object.DontDestroyOnLoad(holder);
            holder.AddComponent<SettingsMenuPoller>();
        }

        [HarmonyPatch(typeof(BasicMenu), nameof(BasicMenu.OnOpen))]
        class BaseMenu_Open
        {
            [HarmonyPrefix]
            static void Prefix(BasicMenu __instance)
            {
                if (settingsMenu.Value is null && __instance.gameObject.name == "Settings Menu")
                    settingsMenu.Value = __instance.gameObject;
            }

            [HarmonyPostfix]
            static void Postfix(BasicMenu __instance)
            {
                if (__instance.gameObject.name == "Settings Menu")
                    OnSettingsMenuOpened?.Invoke();
            }
        }

        [HarmonyPatch(typeof(BasicMenu), nameof(BasicMenu.OnClose))]
        class BaseMenu_Close
        {
            [HarmonyPostfix]
            static void Postfix(BasicMenu __instance)
            {
                if (__instance.gameObject.name == "Settings Menu")
                    OnSettingsMenuClosed?.Invoke();
            }
        }

        class SettingsMenuPoller : MonoBehaviour
        {
            bool wasOpen;
            bool initialized;

            void Update()
            {
                GameObject menu = settingsMenu.Value;
                if (menu == null)
                {
                    BasicMenu[] menus = Object.FindObjectsByType<BasicMenu>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    for (int i = 0; i < menus.Length; i++)
                    {
                        if (menus[i].gameObject.name != "Settings Menu")
                            continue;
                        menu = menus[i].gameObject;
                        settingsMenu.Value = menu;
                        break;
                    }
                }

                bool isOpen = menu != null && menu.activeInHierarchy;
                if (!initialized)
                {
                    initialized = true;
                    wasOpen = isOpen;
                    return;
                }

                if (isOpen == wasOpen)
                    return;
                wasOpen = isOpen;
                if (isOpen)
                    OnSettingsMenuOpened?.Invoke();
                else
                    OnSettingsMenuClosed?.Invoke();
            }
        }
    }
}
