using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI.ModGUI;
using TMPro;
using UITools;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using Type = SFS.UI.ModGUI.Type;

namespace PartEditor
{
    public static class GUI
    {
        public static readonly Part_Local CurrentPart = new ();
        static Part[] currentSelection = Array.Empty<Part>();

        static GameObject holder;
        static Window window;
        public static Window Window => window;
        static readonly List<NumericBinding> numericBindings = new();
        static bool refreshingValues;
        static bool singleMoreExpanded;

        sealed class NumericBinding
        {
            public NumberInput Input;
            public TMP_InputField Field;
            public Func<float> ReadValue;
        }

        static bool setup;
        public static void Setup()
        {
            Vector2Int panelSize = Vector2Int.RoundToInt(BuildTools.TabbedPanel.FrameSize);
            if (!setup && Config.settings.windowSize.Value != panelSize)
                Config.settings.windowSize.Value = panelSize;
            if (!setup)
            {
                Config.settings.windowSize.OnChange += () =>
                {
                    BuildTools.TabbedPanel.SetFrameSize(Config.settings.windowSize.Value);
                    RegenerateWindow(currentSelection);
                };
                Config.settings.stretchToFit.OnChange += () =>
                {
                    BuildTools.TabbedPanel.SetFrameSize(Config.settings.windowSize.Value);
                    RegenerateWindow(currentSelection);
                };
                RegenerateWindow(currentSelection);
            }
            else 
                RegenerateWindow(Array.Empty<Part>());
            setup = true;
        }

        public static void OnSelectionChanged()
        {
            MultiPartGUI.OnSelectionChanged();
            singleMoreExpanded = false;
            currentSelection = BuildManager.main?.selector?.selected?.Where(part => part != null).ToArray()
                ?? Array.Empty<Part>();
            CurrentPart.Value = currentSelection.Length == 1 ? currentSelection[0] : null;
            RegenerateWindow(currentSelection);
        }

        internal static bool SelectionMatches(Part[] expected)
        {
            var liveSelection = BuildManager.main?.selector?.selected;
            return liveSelection != null && liveSelection.Count == expected.Length &&
                   expected.All(part => part != null && liveSelection.Contains(part));
        }

        internal static void RefreshSelection() => RegenerateWindow(currentSelection);

        static async void RegenerateWindow(Part[] selection)
        {
            Vector2Int size = Vector2Int.RoundToInt(BuildTools.TabbedPanel.FrameSize);
            Part part = selection.Length == 1 ? selection[0] : null;
            
            if (holder == null)
            {
                holder = Builder.CreateHolder(Builder.SceneToAttach.CurrentScene, "PartEditor Holder");
                holder.transform.localScale = Vector3.one;
                holder.AddComponent<PartValueRefresher>();
            }

            numericBindings.Clear();
            MultiPartGUI.BeginRebuild();
            Vector2 canvasResolution = UIUtility.CanvasPixelSize;
            if (window == null || window.gameObject == null)
            {
                window = UIToolsBuilder.CreateClosableWindow(holder.transform, 0, size.x, size.y, (int)canvasResolution.x / 2 - 100,
                    (int)canvasResolution.y / 2 - 50, true, false, 1, "Build Tools");
                window.CreateLayoutGroup(Type.Vertical);
                window.EnableScrolling(Type.Vertical);
            }

            // Destroying window content
            for (int i = 0; i < window.ChildrenHolder.childCount; i++)
                Object.Destroy(window.ChildrenHolder.GetChild(i).gameObject);

            BuildTools.TabbedPanel.AddTabs(window, size.x, BuildTools.TabbedPanel.Page.Part,
                PartText.Settings.settings.windowEnabled);

            if (selection.Length > 1)
            {
                window.Size = BuildTools.TabbedPanel.FrameSize;
                MultiPartGUI.Build(window, selection, size);
                await FinishWindowLayout(size);
                return;
            }
            
            if (part == null)
            {
                window.Size = BuildTools.TabbedPanel.FrameSize;
                Builder.CreateLabel(window, size.x - 50, 30, text: "Select a part").Opacity = 0.8f;
                return;
            }

            window.Size = BuildTools.TabbedPanel.FrameSize;
            
            // Position
            Box positionBox = CreateContentBox(window, size.x - 30, "Position");
            CreateNumberInput(positionBox, size.x - 50, 50, 0.8f, "X", part.Position.x, Config.settings.numberChangeStep, ApplyPositionX,
                readValue: () => part.Position.x);
            CreateNumberInput(positionBox, size.x - 50, 50, 0.8f, "Y", part.Position.y, Config.settings.numberChangeStep, ApplyPositionY,
                readValue: () => part.Position.y);
            
            // Orientation
            Box orientationBox = CreateContentBox(window, size.x - 30, "Orientation");
            CreateNumberInput(orientationBox, size.x - 50, 50, 0.8f, "X", part.orientation.orientation.Value.x, Config.settings.numberChangeStep, ApplyOrientationX,
                readValue: () => part.orientation.orientation.Value.x);
            CreateNumberInput(orientationBox, size.x - 50, 50, 0.8f, "Y", part.orientation.orientation.Value.y, Config.settings.numberChangeStep, ApplyOrientationY,
                readValue: () => part.orientation.orientation.Value.y);
            CreateNumberInput(orientationBox, size.x - 50, 50, 0.8f, "Z", part.orientation.orientation.Value.z, -Config.settings.degreeChangeStep, ApplyOrientationZ,
                readValue: () => part.orientation.orientation.Value.z);

            // A small, part-specific editing surface. Only fields actually present on this part
            // are promoted; every other saved variable remains available below by its raw key.
            string partName = new PartSave(part).name;
            var shownNumbers = new HashSet<string>();
            var shownBools = new HashSet<string>();
            Box percentBox = null;
            foreach (string key in new[] { "fuel_percent", "force_percent" })
            {
                if (!part.variablesModule.doubleVariables.GetSaveDictionary().TryGetValue(key, out double fraction))
                    continue;
                percentBox ??= CreateContentBox(window, size.x - 30, partName);
                CreateNumberInput(percentBox, size.x - 50, 50, 0.6f,
                    PartFieldSemantics.PercentLabel(key), (float)(fraction * 100), 1f,
                    percent => ApplyDoubleVariable(key, Mathf.Clamp(percent, 0, 100) / 100f),
                    readValue: () => (float)(part.variablesModule.doubleVariables.GetValue(key) * 100));
                shownNumbers.Add(key);
            }
            if (part.variablesModule.boolVariables.GetSaveDictionary().Keys.Any(key =>
                    key == "engine_on" || key == "gimbal_on" || key == "heat_on__for_creative_use"))
            {
                var booleans = part.variablesModule.boolVariables.GetSaveDictionary();
                Box engineBox = null;
                AddEngineSwitch("engine_on", "Engine enabled");
                AddEngineSwitch("gimbal_on", "Gimbal enabled");
                AddEngineSwitch("heat_on__for_creative_use", "Heat effect (creative)");

                void AddEngineSwitch(string key, string label)
                {
                    if (!booleans.ContainsKey(key))
                        return;
                    engineBox ??= CreateContentBox(window, size.x - 30, partName);
                    Builder.CreateToggleWithLabel(engineBox, size.x - 50, 50,
                        () => part.variablesModule.boolVariables.GetValue(key),
                        () => InvertBoolVariable(key), labelText: label);
                    shownBools.Add(key);
                }
            }

            var visibleNumbers = part.variablesModule.doubleVariables.GetSaveDictionary()
                .Where(save => !shownNumbers.Contains(save.Key) && !PartFieldSemantics.IsMoreOption(save.Key)).ToArray();
            var moreNumbers = part.variablesModule.doubleVariables.GetSaveDictionary()
                .Where(save => !shownNumbers.Contains(save.Key) && PartFieldSemantics.IsMoreOption(save.Key)).ToArray();
            var visibleStrings = part.variablesModule.stringVariables.GetSaveDictionary()
                .Where(save => !PartFieldSemantics.IsMoreOption(save.Key)).ToArray();
            var moreStrings = part.variablesModule.stringVariables.GetSaveDictionary()
                .Where(save => PartFieldSemantics.IsMoreOption(save.Key)).ToArray();
            var visibleBools = part.variablesModule.boolVariables.GetSaveDictionary()
                .Where(save => !shownBools.Contains(save.Key) && !PartFieldSemantics.IsMoreOption(save.Key)).ToArray();
            var moreBools = part.variablesModule.boolVariables.GetSaveDictionary()
                .Where(save => !shownBools.Contains(save.Key) && PartFieldSemantics.IsMoreOption(save.Key)).ToArray();

            AddNumberSection(visibleNumbers, "Other Number Variables");
            AddStringSection(visibleStrings, "Other Text Variables");
            AddBoolSection(visibleBools, "Other Switch Variables");

            int moreCount = moreNumbers.Length + moreStrings.Length + moreBools.Length;
            if (moreCount > 0)
            {
                Builder.CreateButton(window, size.x - 30, 38,
                    onClick: () => { singleMoreExpanded = !singleMoreExpanded; RefreshSelection(); },
                    text: $"{(singleMoreExpanded ? "Hide" : "More")} options ({moreCount})");
                if (singleMoreExpanded)
                {
                    AddNumberSection(moreNumbers, "More Number Variables");
                    AddStringSection(moreStrings, "More Text Variables");
                    AddBoolSection(moreBools, "More Switch Variables");
                }
            }

            void AddNumberSection(IEnumerable<KeyValuePair<string, double>> fields, string title)
            {
                KeyValuePair<string, double>[] items = fields.ToArray();
                if (items.Length == 0) return;
                Box box = CreateContentBox(window, size.x - 30, title);
                foreach (KeyValuePair<string, double> save in items)
                {
                    if (double.IsNaN(save.Value) || double.IsInfinity(save.Value))
                    {
                        Label value = Builder.CreateLabel(box, size.x - 50, 35,
                            text: $"{save.Key}: {save.Value} (non-finite saved value)");
                        value.AutoFontResize = false;
                        value.FontSize = 17;
                        continue;
                    }
                    CreateNumberInput(box, size.x - 50, 50, 0.55f, save.Key, (float)save.Value,
                        Config.settings.numberChangeStep, f => ApplyDoubleVariable(save.Key, f), true, 18,
                        () => (float)part.variablesModule.doubleVariables.GetValue(save.Key));
                }
            }

            void AddStringSection(IEnumerable<KeyValuePair<string, string>> fields, string title)
            {
                KeyValuePair<string, string>[] items = fields.ToArray();
                if (items.Length == 0) return;
                Box box = CreateContentBox(window, size.x - 30, title);
                foreach (KeyValuePair<string, string> save in items)
                {
                    InputWithLabel input = Builder.CreateInputWithLabel(box, size.x - 50, 50, 0, 0,
                        save.Key, save.Value, s => ApplyStringVariable(save.Key, s));
                    input.label.AutoFontResize = false;
                    input.label.FontSize = 18;
                }
            }

            void AddBoolSection(IEnumerable<KeyValuePair<string, bool>> fields, string title)
            {
                KeyValuePair<string, bool>[] items = fields.ToArray();
                if (items.Length == 0) return;
                Box box = CreateContentBox(window, size.x - 30, title);
                foreach (KeyValuePair<string, bool> save in items)
                {
                    ToggleWithLabel toggle = Builder.CreateToggleWithLabel(box, size.x - 50, 50,
                        () => part.variablesModule.boolVariables.GetValue(save.Key),
                        () => InvertBoolVariable(save.Key), labelText: save.Key);
                    toggle.label.AutoFontResize = false;
                    toggle.label.FontSize = 18;
                }
            }

            await FinishWindowLayout(size);
            
            void ApplyPositionX(float x) => part.Position = new Vector2(x, part.Position.y);
            void ApplyPositionY(float y) => part.Position = new Vector2(part.Position.x, y);
            
            void ApplyOrientationX(float x)
            {
                Orientation orientation = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(x, orientation.y, orientation.z);
                part.RegenerateMesh();
            }
            void ApplyOrientationY(float y)
            {
                Orientation orientation = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(orientation.x, y, orientation.z);
                part.RegenerateMesh();
            }
            void ApplyOrientationZ(float z)
            {
                Orientation orientation = part.orientation.orientation.Value;
                part.orientation.orientation.Value = new Orientation(orientation.x, orientation.y, z);
                part.RegenerateMesh();
            }

            void ApplyDoubleVariable(string name, float value)
            {
                part.variablesModule.doubleVariables.SetValue(name, value, (true, true));
                part.RegenerateMesh();
                AdaptModule.UpdateAdaptation(part);
            }
            void ApplyStringVariable(string name, string value)
            {
                part.variablesModule.stringVariables.SetValue(name, value, (true, true));
                part.RegenerateMesh();
                AdaptModule.UpdateAdaptation(part);
            }
            void InvertBoolVariable(string name)
            {
                part.variablesModule.boolVariables.SetValue(name, !part.variablesModule.boolVariables.GetValue(name), (true, true));
                part.RegenerateMesh();
                AdaptModule.UpdateAdaptation(part);
            }
        }

        static async UniTask FinishWindowLayout(Vector2Int size)
        {
            // New multi-edit rows remove their default fitters at end of frame.
            // Rebuild after that removal so the section boxes measure the fixed row heights.
            await UniTask.Yield();
            LayoutRebuilder.ForceRebuildLayoutImmediate(window.ChildrenHolder as RectTransform);
            if (!Config.settings.stretchToFit.Value)
                return;
            await UniTask.Yield();
            await UniTask.Yield();
            int contentHeight = Mathf.CeilToInt(UnityEngine.UI.LayoutUtility.GetPreferredHeight(window.ChildrenHolder as RectTransform) + 60);
            int maximumHeight = Mathf.FloorToInt(Screen.height / BuildSettings.Config.settings.windowScale.Value) - 80;
            BuildTools.TabbedPanel.SetFrameSize(new Vector2Int(size.x,
                Mathf.Clamp(contentHeight, Config.settings.windowSize.Value.y, maximumHeight)));
            window.Size = BuildTools.TabbedPanel.FrameSize;
        }

        internal static Box CreateContentBox(Transform parent, int width, string label)
        {
            Box box = Builder.CreateBox(parent, width, 10);
            box.CreateLayoutGroup(Type.Vertical, spacing: 5f, padding: new RectOffset(0, 0, 5, 5));
            // Enable auto-resizing
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Builder.CreateLabel(box, width, 35, text: label);
            
            return box;
        }

        internal static void RefreshDisplayedValues()
        {
            if (currentSelection.Length > 1)
            {
                MultiPartGUI.RefreshDisplayedValues();
                return;
            }
            if (CurrentPart.Value == null)
                return;

            foreach (NumericBinding binding in numericBindings)
            {
                if (binding.Input.gameObject == null || binding.Field != null && binding.Field.isFocused)
                    continue;
                float currentValue = binding.ReadValue();
                if (Mathf.Abs(binding.Input.Value - currentValue) < 0.00001f)
                    continue;
                try
                {
                    refreshingValues = true;
                    binding.Input.Value = currentValue;
                }
                finally
                {
                    refreshingValues = false;
                }
            }
        }

        public static void CreateNumberInput(Transform parent, int width, int height, float inputWidthRatio, string label, float value, float step, Action<float> onChange, bool fixedFontSize = false, float fontSize = 0, Func<float> readValue = null)
        {
            Container container = Builder.CreateContainer(parent);
            container.CreateLayoutGroup(Type.Horizontal);
            
            Label title = Builder.CreateLabel(container, (int)((width - 20) * (1 - inputWidthRatio)), (int)(height * 0.8f), text: label);
            title.TextAlignment = TextAlignmentOptions.MidlineLeft;
            if (fixedFontSize)
            {
                title.AutoFontResize = false;
                title.FontSize = fontSize;
            }
            
            NumberInput input = UIToolsBuilder.CreateNumberInput(container, (int)((width - 20) * inputWidthRatio), height,
                value, step);
            TMP_InputField field = input.gameObject.GetComponentInChildren<TMP_InputField>(true);
            bool normalizing = false;
            input.OnValueChangedEvent += changedValue =>
            {
                if (refreshingValues || normalizing)
                    return;
                // Button steps can accumulate float error (for example 8.200001 after 0.1 clicks).
                // Preserve exact manually typed values while the input field has focus.
                float applied = field != null && field.isFocused ? changedValue : SnapStepNoise(changedValue, step);
                if (applied != changedValue)
                {
                    try
                    {
                        normalizing = true;
                        input.Value = applied;
                    }
                    finally
                    {
                        normalizing = false;
                    }
                }
                onChange(applied);
            };
            if (readValue != null)
                numericBindings.Add(new NumericBinding
                {
                    Input = input,
                    Field = field,
                    ReadValue = readValue
                });
        }

        static float SnapStepNoise(float value, float step)
        {
            double magnitude = Math.Abs((double)step);
            if (magnitude <= 0 || float.IsNaN(value) || float.IsInfinity(value))
                return value;
            double candidate = Math.Round((double)value / magnitude) * magnitude;
            float snapped = (float)candidate;
            return Math.Abs((double)value - snapped) <= magnitude * 0.0001 ? snapped : value;
        }
    }

    public sealed class PartValueRefresher : MonoBehaviour
    {
        void Update() => GUI.RefreshDisplayedValues();
    }
}
