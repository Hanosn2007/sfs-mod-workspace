using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SFS.Builds;
using SFS.Parts;
using SFS.Parts.Modules;
using SFS.UI.ModGUI;
using TMPro;
using UITools;
using UnityEngine;
using Type = SFS.UI.ModGUI.Type;

namespace PartEditor
{
    internal static class MultiPartGUI
    {
        enum FieldKind { Number, Toggle, Text }
        static bool advancedExpanded;
        static readonly List<Action> refreshers = new();

        sealed class FieldGroup
        {
            public string PartName;
            public string Key;
            public FieldKind Kind;
            public readonly List<Part> Parts = new();
        }

        internal static void Build(Window window, Part[] selection, Vector2Int size)
        {
            refreshers.Clear();
            int width = size.x - 30;
            Label countLabel = Builder.CreateLabel(window, width, 40, text: $"{selection.Length} parts selected");
            countLabel.AutoFontResize = false;
            countLabel.FontSize = 26;

            Box moveBox = GUI.CreateContentBox(window, width, "Move selection");
            Container moveRow = Builder.CreateContainer(moveBox);
            moveRow.CreateLayoutGroup(Type.Horizontal, spacing: 4);
            Builder.CreateLabel(moveRow, 45, 38, text: "Step");
            TextInput moveStep = Builder.CreateTextInput(moveRow, 65, 38, text: "1");
            Builder.CreateButton(moveRow, 52, 38, onClick: () => Nudge(selection, moveStep, -1, 0), text: "X-");
            Builder.CreateButton(moveRow, 52, 38, onClick: () => Nudge(selection, moveStep, 1, 0), text: "X+");
            Builder.CreateButton(moveRow, 52, 38, onClick: () => Nudge(selection, moveStep, 0, -1), text: "Y-");
            Builder.CreateButton(moveRow, 52, 38, onClick: () => Nudge(selection, moveStep, 0, 1), text: "Y+");
            AddLabel(moveBox, width - 20, "Each tap moves all selected parts together.");

            List<FieldGroup> groups = CollectFields(selection);
            FieldGroup[] featured = groups.Where(IsFeatured).ToArray();
            FieldGroup[] raw = groups.Where(group => !IsFeatured(group)).ToArray();
            FieldGroup[] shared = featured.Where(group => group.Parts.Count == selection.Length).ToArray();
            FieldGroup[] partial = featured.Where(group => group.Parts.Count < selection.Length).ToArray();

            if (shared.Length > 0)
            {
                Box sharedBox = GUI.CreateContentBox(window, width, "Common controls");
                foreach (FieldGroup group in shared)
                    AddFieldRow(sharedBox, group, selection, size.x);
            }

            foreach (IGrouping<string, FieldGroup> typeGroup in partial.GroupBy(group => group.PartName)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Box partialBox = GUI.CreateContentBox(window, width, typeGroup.Key);
                foreach (FieldGroup group in typeGroup)
                    AddFieldRow(partialBox, group, selection, size.x);
            }

            Builder.CreateButton(window, width, 38,
                onClick: () => { advancedExpanded = !advancedExpanded; GUI.RefreshSelection(); },
                text: $"{(advancedExpanded ? "Hide" : "Show")} raw fields ({raw.Length})");
            if (!advancedExpanded)
                return;

            foreach (IGrouping<string, FieldGroup> typeGroup in raw.GroupBy(group => group.PartName)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Box rawBox = GUI.CreateContentBox(window, width, $"{typeGroup.Key} - raw fields");
                foreach (FieldGroup group in typeGroup)
                    AddFieldRow(rawBox, group, selection, size.x);
            }
        }

        static List<FieldGroup> CollectFields(Part[] selection)
        {
            var groups = new Dictionary<(string name, FieldKind kind, string key), FieldGroup>();
            int unknownIndex = 0;
            foreach (Part part in selection)
            {
                string partName = new PartSave(part).name;
                if (string.IsNullOrWhiteSpace(partName))
                    partName = $"Unknown part {++unknownIndex}";
                foreach (string key in part.variablesModule.doubleVariables.GetSaveDictionary().Keys)
                    Add(partName, FieldKind.Number, key, part);
                foreach (string key in part.variablesModule.boolVariables.GetSaveDictionary().Keys)
                    Add(partName, FieldKind.Toggle, key, part);
                foreach (string key in part.variablesModule.stringVariables.GetSaveDictionary().Keys)
                    Add(partName, FieldKind.Text, key, part);
            }
            return groups.Values.OrderBy(group => group.PartName, StringComparer.Ordinal)
                .ThenBy(group => group.Kind).ThenBy(group => group.Key, StringComparer.Ordinal).ToList();

            void Add(string name, FieldKind kind, string key, Part part)
            {
                var id = (name, kind, key);
                if (!groups.TryGetValue(id, out FieldGroup group))
                {
                    group = new FieldGroup { PartName = name, Kind = kind, Key = key };
                    groups.Add(id, group);
                }
                group.Parts.Add(part);
            }
        }

        static void AddFieldRow(Box box, FieldGroup group, Part[] selection, int frameWidth)
        {
            string label = FieldLabel(group);
            string applicability = $"Applies {group.Parts.Count}/{selection.Length}";
            int controlWidth = Mathf.Max(115, frameWidth - 180);
            AddLabel(box, frameWidth - 50, $"{label} · {applicability}");
            Label state = AddLabel(box, frameWidth - 50, "");
            if (group.Kind == FieldKind.Toggle)
            {
                Container toggleRow = Builder.CreateContainer(box);
                toggleRow.CreateLayoutGroup(Type.Horizontal, spacing: 5);
                Builder.CreateButton(toggleRow, 90, 38, onClick: () => ApplyToggle(group, selection, false), text: "Set Off");
                Builder.CreateButton(toggleRow, 90, 38, onClick: () => ApplyToggle(group, selection, true), text: "Set On");
                refreshers.Add(() => SetStatus(state, ToggleState(group)));
                state.Text = ToggleState(group);
                return;
            }

            if (group.Kind == FieldKind.Number)
            {
                Container numberRow = Builder.CreateContainer(box);
                numberRow.CreateLayoutGroup(Type.Horizontal, spacing: 5);
                string rendered = NumberState(group);
                TextInput value = Builder.CreateTextInput(numberRow, controlWidth, 40, text: rendered);
                value.field.onEndEdit.AddListener(_ =>
                {
                    if (value.Text != rendered)
                        ApplyNumber(group, selection, value);
                });
                double step = IsFuelPercent(group) ? 1 : Config.settings.numberChangeStep.Value;
                Builder.CreateButton(numberRow, 38, 38, onClick: () => NudgeNumber(group, selection, -step), text: "-");
                Builder.CreateButton(numberRow, 38, 38, onClick: () => NudgeNumber(group, selection, step), text: "+");
                refreshers.Add(() =>
                {
                    if (value.gameObject == null || value.field.isFocused) return;
                    string current = NumberState(group);
                    if (value.Text != current) value.Text = current;
                    rendered = current;
                    SetStatus(state, current == "" ? "Mixed · enter a value or use +/-" : "Enter or leave field to update · +/- adjusts each part");
                });
                state.Text = NumberState(group) == "" ? "Mixed · enter a value or use +/-" : "Enter or leave field to update · +/- adjusts each part";
                return;
            }

            Container textRow = Builder.CreateContainer(box);
            textRow.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            string displayedText = TextState(group);
            TextInput text = Builder.CreateTextInput(textRow, Mathf.Max(150, frameWidth - 120), 40, text: displayedText);
            text.field.onEndEdit.AddListener(_ =>
            {
                if (text.Text != displayedText)
                    ApplyText(group, selection, text);
            });
            Builder.CreateButton(textRow, 55, 38, onClick: () => SetText(group, selection, ""), text: "Clear");
            refreshers.Add(() =>
            {
                if (text.gameObject == null || text.field.isFocused) return;
                string current = TextState(group);
                if (text.Text != current) text.Text = current;
                displayedText = current;
                SetStatus(state, TextMixed(group) ? "Mixed · type to set all applicable parts" : "Enter or leave field to update");
            });
            state.Text = TextMixed(group) ? "Mixed · type to set all applicable parts" : "Enter or leave field to update";
        }

        internal static void RefreshDisplayedValues()
        {
            foreach (Action refresh in refreshers)
                refresh();
        }

        static void SetStatus(Label label, string text)
        {
            if (label.gameObject != null && label.Text != text)
                label.Text = text;
        }

        static Label AddLabel(Transform parent, int width, string text)
        {
            Label label = Builder.CreateLabel(parent, width, 35, text: text);
            label.AutoFontResize = false;
            label.FontSize = 17;
            label.TextAlignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        static bool IsFeatured(FieldGroup group) => IsFuelPercent(group) ||
            group.PartName == "Engine Titan" && group.Kind == FieldKind.Toggle &&
            (group.Key == "engine_on" || group.Key == "gimbal_on" || group.Key == "heat_on__for_creative_use");

        static string ToggleState(FieldGroup group)
        {
            bool first = group.Parts[0].variablesModule.boolVariables.GetValue(group.Key);
            return group.Parts.Any(part => part.variablesModule.boolVariables.GetValue(group.Key) != first)
                ? "Mixed · choose On or Off" : first ? "On" : "Off";
        }

        static string NumberState(FieldGroup group)
        {
            double first = ReadDisplayNumber(group, group.Parts[0]);
            return group.Parts.Any(part => ReadDisplayNumber(group, part) != first)
                ? "" : first.ToString("G8", CultureInfo.InvariantCulture);
        }

        static bool TextMixed(FieldGroup group)
        {
            string first = group.Parts[0].variablesModule.stringVariables.GetValue(group.Key);
            return group.Parts.Any(part => !string.Equals(
                part.variablesModule.stringVariables.GetValue(group.Key), first, StringComparison.Ordinal));
        }

        static string TextState(FieldGroup group) => TextMixed(group) ? "" :
            group.Parts[0].variablesModule.stringVariables.GetValue(group.Key);

        static string FieldLabel(FieldGroup group)
        {
            if (group.PartName == "Fuel Tank" && group.Kind == FieldKind.Number && group.Key == "fuel_percent")
                return "Fuel (%)";
            if (group.PartName == "Engine Titan" && group.Kind == FieldKind.Toggle)
                return group.Key switch
                {
                    "engine_on" => "Engine enabled",
                    "gimbal_on" => "Gimbal enabled",
                    "heat_on__for_creative_use" => "Heat effect",
                    _ => group.Key
                };
            string type = group.Kind switch
            {
                FieldKind.Number => "number",
                FieldKind.Toggle => "switch",
                _ => "text"
            };
            return $"{group.Key} [{type}]";
        }

        static bool IsFuelPercent(FieldGroup group) => group.PartName == "Fuel Tank" &&
                                                        group.Kind == FieldKind.Number && group.Key == "fuel_percent";

        static double ReadDisplayNumber(FieldGroup group, Part part)
        {
            double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
            return IsFuelPercent(group) ? raw * 100 : raw;
        }

        static void Nudge(Part[] selection, TextInput stepInput, int directionX, int directionY)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            if (!TryFiniteFloat(stepInput.Text, out float step) || step <= 0)
            {
                Message("Enter a positive move step");
                return;
            }
            Vector2 offset = new(step * directionX, step * directionY);
            Undo.main.RecordStatChangeStep(selection, () =>
            {
                foreach (Part part in selection)
                    part.Position += offset;
            });
        }

        static void ApplyNumber(FieldGroup group, Part[] selection, TextInput input)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            if (string.IsNullOrWhiteSpace(input.Text))
                return;
            if (input.Text == NumberState(group))
                return;
            if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ||
                double.IsNaN(value) || double.IsInfinity(value) ||
                IsFuelPercent(group) && (value < 0 || value > 100))
            {
                Message(IsFuelPercent(group) ? "Fuel must be from 0 to 100" : "Enter a valid number");
                return;
            }
            double raw = IsFuelPercent(group) ? value / 100 : value;
            Part[] targets = NumberTargets(group);
            if (targets.Length == 0)
                return;
            if (targets.All(part => part.variablesModule.doubleVariables.GetValue(group.Key) == raw))
                return;
            Undo.main.RecordStatChangeStep(targets, () =>
            {
                foreach (Part part in targets)
                {
                    part.variablesModule.doubleVariables.SetValue(group.Key, raw, (true, true));
                    RefreshPart(part);
                }
            });
        }

        static void NudgeNumber(FieldGroup group, Part[] selection, double step)
        {
            if (!GUI.SelectionMatches(selection) || step == 0)
                return;
            Part[] targets = NumberTargets(group);
            if (targets.Length == 0)
                return;
            double change = IsFuelPercent(group) ? step / 100 : step;
            if (targets.All(part =>
            {
                double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
                double next = IsFuelPercent(group) ? Math.Max(0, Math.Min(1, raw + change)) : raw + change;
                return double.IsNaN(next) || double.IsInfinity(next) || next == raw;
            }))
                return;
            Undo.main.RecordStatChangeStep(targets, () =>
            {
                foreach (Part part in targets)
                {
                    double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
                    double next = IsFuelPercent(group) ? Math.Max(0, Math.Min(1, raw + change)) : raw + change;
                    if (double.IsNaN(next) || double.IsInfinity(next) || next == raw)
                        continue;
                    part.variablesModule.doubleVariables.SetValue(group.Key, next, (true, true));
                    RefreshPart(part);
                }
            });
        }

        static Part[] NumberTargets(FieldGroup group) => group.Parts.Where(part => part != null &&
            part.variablesModule.doubleVariables.GetSaveDictionary().ContainsKey(group.Key)).ToArray();

        static void ApplyToggle(FieldGroup group, Part[] selection, bool value)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            Part[] targets = group.Parts.Where(part => part != null &&
                part.variablesModule.boolVariables.GetSaveDictionary().ContainsKey(group.Key)).ToArray();
            if (targets.Length == 0)
                return;
            if (targets.All(part => part.variablesModule.boolVariables.GetValue(group.Key) == value))
                return;
            Undo.main.RecordStatChangeStep(targets, () =>
            {
                foreach (Part part in targets)
                {
                    part.variablesModule.boolVariables.SetValue(group.Key, value, (true, true));
                    RefreshPart(part);
                }
            });
        }

        static void ApplyText(FieldGroup group, Part[] selection, TextInput input)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            if (TextMixed(group) && input.Text == "")
                return;
            if (input.Text == TextState(group))
                return;
            SetText(group, selection, input.Text);
        }

        static void SetText(FieldGroup group, Part[] selection, string value)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            Part[] targets = group.Parts.Where(part => part != null &&
                part.variablesModule.stringVariables.GetSaveDictionary().ContainsKey(group.Key)).ToArray();
            if (targets.Length == 0)
                return;
            if (targets.All(part => part.variablesModule.stringVariables.GetValue(group.Key) == value))
                return;
            Undo.main.RecordStatChangeStep(targets, () =>
            {
                foreach (Part part in targets)
                {
                    part.variablesModule.stringVariables.SetValue(group.Key, value, (true, true));
                    RefreshPart(part);
                }
            });
        }

        static void RefreshPart(Part part)
        {
            part.RegenerateMesh();
            AdaptModule.UpdateAdaptation(part);
        }

        static bool TryFiniteFloat(string text, out float value)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !float.IsNaN(value) && !float.IsInfinity(value);
        }

        static void Message(string text) => SFS.UI.MsgDrawer.main.Log(text);
    }
}
