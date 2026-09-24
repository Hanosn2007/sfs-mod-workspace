using System;
using System.Linq;
using Newtonsoft.Json;
using SFS.Builds;
using SFS.Parsers.Json;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI.ModGUI;
using SFS.World;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Button = SFS.UI.ModGUI.Button;
using Type = SFS.UI.ModGUI.Type;

namespace PartText
{
    public static class UI
    {
        private static readonly int windowID = Builder.GetRandomID();
        public static GameObject windowHolder;
        public static ClosableWindow window;
        public static TextInput input;
        public static Button savePartButton;
        public static Label noSelectedPartText;
        private static Label fontSizeLabel;
        public static Part currentPart;
        private static Color defaultWindowColor;
        private static bool changingPart = false;
        public static bool editingText = false;
        public static bool IsEditingText
        {
            get
            {
                if (editingText || input != null && input.field != null && input.field.isFocused)
                    return true;
                GameObject selected = EventSystem.current?.currentSelectedGameObject;
                return selected != null &&
                       (selected.GetComponentInParent<TMP_InputField>() != null ||
                        selected.GetComponentInParent<UnityEngine.UI.InputField>() != null);
            }
        }

        public static void SetTextSize(int size)
        {
            Settings.settings.textSize = Mathf.Clamp(size, 16, 40);
            if (fontSizeLabel != null)
                fontSizeLabel.Text = Settings.settings.textSize.ToString();
            if (input?.field != null)
            {
                input.field.pointSize = Settings.settings.textSize;
                input.field.ForceLabelUpdate();
            }
            Settings.Save?.Invoke();
        }

        internal static void ResizeTo(Vector2 size)
        {
            if (input != null)
                input.Size = new Vector2(size.x - 10, size.y - 235);
            if (savePartButton != null)
                savePartButton.Size = new Vector2(size.x - 20, 30);
            if (noSelectedPartText != null)
                noSelectedPartText.Size = new Vector2(size.x - 20, 40);
            if (window?.ChildrenHolder is RectTransform content)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(content);
                Canvas.ForceUpdateCanvases();
            }
        }

        public static void OnBuildLoaded()
        {
            BuildManager.main.selector.onSelectedChange += UpdateCurrentPart;

            Vector2Int windowSize = Vector2Int.RoundToInt(BuildTools.TabbedPanel.FrameSize);
            windowHolder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "Part Text GUI Holder");
            window = UIToolsBuilder.CreateClosableWindow
            (
                windowHolder.transform, windowID,
                windowSize.x,
                windowSize.y,
                100,
                100,
                true,
                true,
                Settings.settings.windowOpacity,
                "Build Tools"
            );
            window.RegisterPermanentSaving(Entrypoint.Main.ModNameID);
            window.RegisterOnDropListener(Settings.Save);
            window.RegisterOnDropListener(() => KeepWindowInView(window));
            defaultWindowColor = window.WindowColor;
            window.CreateLayoutGroup(Type.Vertical, spacing: 5, padding: new RectOffset(5, 5, 5, 5));
            BuildTools.TabbedPanel.AddTabs(window, windowSize.x, BuildTools.TabbedPanel.Page.Json, true);

            noSelectedPartText = Builder.CreateLabel(window, windowSize.x - 20, 40, text: "Select a part...");
            noSelectedPartText.AutoFontResize = false;
            noSelectedPartText.FontSize = 26;
            noSelectedPartText.TextAlignment = TextAlignmentOptions.Center;

            Container fontControls = Builder.CreateContainer(window);
            fontControls.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            Builder.CreateLabel(fontControls, 80, 35, text: "JSON font");
            Builder.CreateButton(fontControls, 45, 35,
                onClick: () => SetTextSize(Settings.settings.textSize - 2), text: "-");
            fontSizeLabel = Builder.CreateLabel(fontControls, 55, 35,
                text: Settings.settings.textSize.ToString());
            fontSizeLabel.TextAlignment = TextAlignmentOptions.Center;
            Builder.CreateButton(fontControls, 45, 35,
                onClick: () => SetTextSize(Settings.settings.textSize + 2), text: "+");

            input = Builder.CreateTextInput(window, windowSize.x - 10, windowSize.y - 235, text: "", onChange: _ => { RemoveInvalidCharacters(); UpdateColor(); });
            input.field.onFocusSelectAll = false;
            input.field.onSelect.AddListener(_ => input.field.caretPosition = TMP_TextUtilities.GetCursorIndexFromPosition(input.field.textComponent, (Vector2)Input.mousePosition, null));
            input.field.onSelect.AddListener(_ => editingText = true);
            input.field.onDeselect.AddListener(_ => editingText = false);
            
            input.field.caretWidth = 2;
            input.field.pointSize = Settings.settings.textSize;
            input.field.textComponent.alignment = TextAlignmentOptions.TopLeft;
            input.field.lineType = TMP_InputField.LineType.MultiLineNewline;
            var scrollTarget = input.gameObject.AddComponent<SFS.UI.Button>();
            scrollTarget.clickEvent = new SFS.UI.ClickUnityEvent();
            scrollTarget.holdEvent = new SFS.UI.HoldUnityEvent();
            scrollTarget.noClickSound = true;
            scrollTarget.onScroll += scroll =>
            {
                if (EventSystem.current == null)
                    return;
                input.field.OnScroll(new PointerEventData(EventSystem.current)
                {
                    scrollDelta = new Vector2(0, -scroll)
                });
            };
            input.Active = false;

            savePartButton = Builder.CreateButton(window, windowSize.x - 20, 30, onClick: ApplyChanges, text: "Save Part");
            savePartButton.Active = false;

            window.Minimized = Settings.settings.windowMinimized;
            UpdateCurrentPart();
        }

        public static void OnBuildUnloaded()
        {
            Settings.Save();
            if (BuildManager.main != null && BuildManager.main.selector != null)
                BuildManager.main.selector.onSelectedChange -= UpdateCurrentPart;
            Object.Destroy(windowHolder);
            editingText = false;
            currentPart = null;
            input = null;
            fontSizeLabel = null;
        }

        public static void ApplyChanges()
        {
            if (CheckValidJson(input.Text))
            {
                changingPart = true;
                Undo.main.CreateNewStep("Edit Part");
                bool clippingCheat = SandboxSettings.main.settings.partClipping;
                SandboxSettings.main.settings.partClipping = false;

                currentPart.aboutToDestroy?.Invoke(currentPart);
                BuildManager.main.buildGrid.RemoveParts(enableNonIntersecting: true, applyUndo: true, currentPart);
                BuildManager.main.selector.Deselect(currentPart);
                currentPart.DestroyPart(createExplosion: false, updateJoints: false, DestructionReason.Intentional);

                PartSave newPartSave = JsonWrapper.FromJson<PartSave>(input.Text);
                Part newPart = PartsLoader.CreatePart(newPartSave, BuildManager.main.buildGrid.activeGrid.transform, BuildManager.main.buildGrid.activeGrid.selectedLayer, OnPartNotOwned.Delete, out OwnershipState _);
                BuildManager.main.buildGrid.AddParts(true, false, true, newPart);
                SandboxSettings.main.settings.partClipping = clippingCheat;
                changingPart = false;
                BuildManager.main.selector.Select(newPart);
            }
            else
            {
                SFS.UI.MsgDrawer.main.Log("Part JSON is invalid");
            }
        }

        private static bool CheckValidJson(string json)
        {
            bool successfulJsonConversion = false;
            try
            {
                successfulJsonConversion = JsonConvert.DeserializeObject<PartSave>(json) != null;
            }
            catch
            {
                // ignored
            }
            return successfulJsonConversion;
        }
        
        private static void RemoveInvalidCharacters()
        {
            input.Text = input.Text.Replace("", "");
        }

        private static void UpdateColor()
        {
            window.WindowColor = currentPart != null && !CheckValidJson(input.Text) ? Color.red : defaultWindowColor;
        }

        public static void UpdateCurrentPart()
        {
            try
            {
                if (changingPart)
                    return;

                Part selectedPart = BuildManager.main.selector.selected.Count == 1
                    ? BuildManager.main.selector.selected.First()
                    : null;
                if (currentPart == selectedPart)
                    return;
                currentPart = selectedPart;

                window.WindowColor = defaultWindowColor;
                noSelectedPartText.Active = !currentPart;
                input.Active = currentPart;
                savePartButton.Active = currentPart;

                input.Text = currentPart ? JsonWrapper.ToJson(new PartSave(currentPart), true) : "";
            }
            catch (NullReferenceException) { }

        }

        private static void KeepWindowInView(Window window)
        {
            try
            {
                RectTransform rect = window.rectTransform;
                Vector2 center = rect.rect.center / 2;
                rect.position = Vector2.Max((Vector2)rect.position + center, Vector2.zero) - center;
                rect.position = Vector2.Min((Vector2)rect.position + center, new Vector2(Screen.width, Screen.height)) - center;
            }
            catch
            {
                // ignored
            }
        }
    }
}
