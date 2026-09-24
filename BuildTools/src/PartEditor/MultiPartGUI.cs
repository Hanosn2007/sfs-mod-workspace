using System;
using System.Collections.Generic;
using System.Globalization;
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
    internal static class MultiPartGUI
    {
        enum FieldKind { Number, Toggle, Text }
        static bool advancedExpanded;
        static string moveStepText = "1";
        static int selectionVersion;
        static readonly List<Action> refreshers = new();
        static readonly Dictionary<(string partName, FieldKind kind, string key, bool raw), Draft> drafts = new();
        static bool rebuilding;
        static int rebuildRevision;

        sealed class Draft
        {
            public string Rendered;
            public string Text;
            public bool Dirty;
            public bool Error;
            public int Pending;
            public double[] NumberBaseline;
            public string[] TextBaseline;
        }

        internal static void OnSelectionChanged()
        {
            selectionVersion++;
            refreshers.Clear();
            drafts.Clear();
            advancedExpanded = false;
        }

        internal static void BeginRebuild()
        {
            rebuilding = true;
            ResetRebuilding(++rebuildRevision).Forget();
        }

        static async UniTaskVoid ResetRebuilding(int revision)
        {
            await UniTask.Yield();
            await UniTask.Yield();
            if (rebuildRevision == revision)
                rebuilding = false;
        }

        static Draft GetDraft(FieldGroup group, bool rawMode, string current)
        {
            var id = (group.PartName, group.Kind, group.Key, rawMode);
            if (!drafts.TryGetValue(id, out Draft draft))
            {
                draft = new Draft { Rendered = current, Text = current };
                drafts.Add(id, draft);
            }
            return draft;
        }

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
            Label countLabel = Builder.CreateLabel(window, width, 35, text: $"{selection.Length} parts selected");
            countLabel.AutoFontResize = false;
            countLabel.FontSize = 22;

            int sharedRowWidth = size.x - 50;
            int sharedLabelWidth = Mathf.Clamp(Mathf.RoundToInt(sharedRowWidth * 0.42f), 150, 200);
            int sharedControlWidth = sharedRowWidth - sharedLabelWidth - 8;
            Box positionBox = CreateSection(window, width, "Position · relative move");
            TextInput xStep = null, yStep = null;
            xStep = AddMoveAxis(positionBox, selection, sharedRowWidth, sharedLabelWidth, sharedControlWidth,
                "X", -1, 0, 1, 0);
            yStep = AddMoveAxis(positionBox, selection, sharedRowWidth, sharedLabelWidth, sharedControlWidth,
                "Y", 0, -1, 0, 1);
            xStep.field.onValueChanged.AddListener(value =>
            {
                moveStepText = value;
                yStep.field.SetTextWithoutNotify(value);
            });
            yStep.field.onValueChanged.AddListener(value =>
            {
                moveStepText = value;
                xStep.field.SetTextWithoutNotify(value);
            });

            Box orientationBox = CreateSection(window, width, "Orientation");
            AddOrientationRow(orientationBox, selection, size.x, 0, "X", Config.settings.numberChangeStep.Value);
            AddOrientationRow(orientationBox, selection, size.x, 1, "Y", Config.settings.numberChangeStep.Value);
            AddOrientationRow(orientationBox, selection, size.x, 2, "Z", -Config.settings.degreeChangeStep.Value);

            List<FieldGroup> groups = CollectFields(selection);
            FieldGroup[] visible = groups.Where(group => !IsMoreOption(group)).ToArray();
            FieldGroup[] more = groups.Where(IsMoreOption).ToArray();
            foreach (IGrouping<string, FieldGroup> typeGroup in visible.GroupBy(group => group.PartName)
                         .OrderBy(group => group.Key == "Fuel Tank" ? 0 : 1)
                         .ThenBy(group => group.Key, StringComparer.Ordinal))
            {
                Box partialBox = CreateSection(window, width, typeGroup.Key);
                foreach (FieldGroup group in typeGroup)
                    AddFieldRow(partialBox, group, selection, size.x, false);
            }

            foreach (string partName in selection.Select(part => new PartSave(part).name)
                         .Distinct(StringComparer.Ordinal)
                         .Where(name => !visible.Any(group => group.PartName == name))
                         .OrderBy(name => name, StringComparer.Ordinal))
            {
                Box emptyBox = CreateSection(window, width, partName);
                AddLabel(emptyBox, width - 20,
                    groups.Any(group => group.PartName == partName)
                        ? "Saved fields are in More options"
                        : "No saved variable fields", 30, 17);
            }

            if (more.Length == 0)
                return;
            Builder.CreateButton(window, width, 38,
                onClick: () => { advancedExpanded = !advancedExpanded; GUI.RefreshSelection(); },
                text: $"{(advancedExpanded ? "Hide" : "More")} options ({more.Length})");
            if (!advancedExpanded)
                return;

            foreach (IGrouping<string, FieldGroup> typeGroup in more.GroupBy(group => group.PartName)
                         .OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                Box rawBox = CreateSection(window, width, $"{typeGroup.Key} · raw");
                foreach (FieldGroup group in typeGroup)
                    AddFieldRow(rawBox, group, selection, size.x, true);
            }
        }

        static bool IsMoreOption(FieldGroup group) => PartFieldSemantics.IsMoreOption(group.Key);

        static Box CreateSection(Window window, int width, string title)
        {
            Box box = Builder.CreateBox(window, width, 10);
            box.CreateLayoutGroup(Type.Vertical, spacing: 5f, padding: new RectOffset(5, 5, 5, 5));
            box.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            Label heading = Builder.CreateLabel(box, width - 20, 35, text: title);
            heading.AutoFontResize = false;
            heading.FontSize = 24;
            return box;
        }

        static TextInput AddMoveAxis(Box box, Part[] selection, int rowWidth, int labelWidth,
            int controlWidth, string label, int negativeX, int negativeY, int positiveX, int positiveY)
        {
            Container row = Builder.CreateContainer(box);
            Object.Destroy(row.gameObject.GetComponent<ContentSizeFitter>());
            row.CreateLayoutGroup(Type.Horizontal, spacing: 8);
            row.Size = new Vector2(rowWidth, 52);
            LayoutElement layout = row.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = rowWidth;
            layout.preferredHeight = 52;
            AddLabel(row, labelWidth, label, 50, 22);
            Container control = Builder.CreateContainer(row);
            Object.Destroy(control.gameObject.GetComponent<ContentSizeFitter>());
            control.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            control.Size = new Vector2(controlWidth, 44);
            LayoutElement controlLayout = control.gameObject.AddComponent<LayoutElement>();
            controlLayout.preferredWidth = controlWidth;
            controlLayout.preferredHeight = 44;
            TextInput step = null;
            Builder.CreateButton(control, 32, 32, onClick: () => Nudge(selection, step, negativeX, negativeY), text: "<");
            step = Builder.CreateTextInput(control, controlWidth - 74, 44, text: moveStepText);
            Builder.CreateButton(control, 32, 32, onClick: () => Nudge(selection, step, positiveX, positiveY), text: ">");
            return step;
        }

        static void AddOrientationRow(Box box, Part[] selection, int frameWidth, int axis, string label, float step)
        {
            int rowWidth = frameWidth - 50;
            int labelWidth = Mathf.Clamp(Mathf.RoundToInt(rowWidth * 0.42f), 150, 200);
            int controlWidth = rowWidth - labelWidth - 8;
            string key = $"orientation_{axis}";
            var id = ("All Parts", FieldKind.Number, key, false);
            if (!drafts.TryGetValue(id, out Draft draft))
            {
                string initial = OrientationState(selection, axis);
                draft = new Draft { Rendered = initial, Text = initial };
                drafts.Add(id, draft);
            }

            Container row = Builder.CreateContainer(box);
            Object.Destroy(row.gameObject.GetComponent<ContentSizeFitter>());
            row.CreateLayoutGroup(Type.Horizontal, spacing: 8);
            row.Size = new Vector2(rowWidth, 52);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredWidth = rowWidth;
            rowLayout.preferredHeight = 52;
            AddLabel(row, labelWidth, label, 50, 22);
            Container control = Builder.CreateContainer(row);
            Object.Destroy(control.gameObject.GetComponent<ContentSizeFitter>());
            control.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            control.Size = new Vector2(controlWidth, 44);
            LayoutElement controlLayout = control.gameObject.AddComponent<LayoutElement>();
            controlLayout.preferredWidth = controlWidth;
            controlLayout.preferredHeight = 44;
            TextInput input = null;
            Label error = null;
            Builder.CreateButton(control, 32, 32,
                onClick: () => StepOrientation(selection, axis, draft, input, error, -step), text: "<");
            input = Builder.CreateTextInput(control, controlWidth - 74, 44, text: draft.Text);
            SetPlaceholder(input, "Mixed");
            error = AddLabel(box, rowWidth, "Invalid orientation · not applied", 25, 14);
            error.Active = draft.Error;
            input.field.onValueChanged.AddListener(changed =>
            {
                if (!draft.Dirty)
                    draft.NumberBaseline = selection.Select(part => (double)ReadOrientation(part, axis)).ToArray();
                draft.Text = changed;
                draft.Dirty = changed != draft.Rendered;
                draft.Error = false;
                error.Active = false;
            });
            int generation = selectionVersion;
            input.field.onEndEdit.AddListener(_ =>
            {
                if (!rebuilding && draft.Dirty)
                    CommitOrientationLater(selection, axis, draft, input, error, generation).Forget();
            });
            Builder.CreateButton(control, 32, 32,
                onClick: () => StepOrientation(selection, axis, draft, input, error, step), text: ">");
            refreshers.Add(() =>
            {
                if (input.gameObject == null) return;
                if (draft.Dirty)
                {
                    if (draft.NumberBaseline != null && selection.Where((part, index) =>
                            ReadOrientation(part, axis) != draft.NumberBaseline[index]).Any())
                    {
                        ResetDraft(draft, input, OrientationState(selection, axis), error);
                        Message("Orientation changed outside the editor; draft cancelled");
                    }
                    return;
                }
                if (input.field.isFocused) return;
                string current = OrientationState(selection, axis);
                if (input.Text != current) input.field.SetTextWithoutNotify(current);
                draft.Rendered = draft.Text = current;
            });
        }

        static float ReadOrientation(Part part, int axis)
        {
            Orientation value = part.orientation.orientation.Value;
            return axis == 0 ? value.x : axis == 1 ? value.y : value.z;
        }

        static string OrientationState(Part[] selection, int axis)
        {
            float first = ReadOrientation(selection[0], axis);
            return selection.Any(part => ReadOrientation(part, axis) != first)
                ? "" : first.ToString("G8", CultureInfo.InvariantCulture);
        }

        static async UniTaskVoid CommitOrientationLater(Part[] selection, int axis, Draft draft,
            TextInput input, Label error, int generation)
        {
            int pending = ++draft.Pending;
            if (Input.GetMouseButton(0))
                await UniTask.WaitUntil(() => !Input.GetMouseButton(0));
            await UniTask.Yield();
            await UniTask.Yield();
            if (draft.Pending != pending || selectionVersion != generation || rebuilding ||
                input.gameObject == null || !GUI.SelectionMatches(selection) || !draft.Dirty)
                return;
            if (input.field.wasCanceled || string.IsNullOrWhiteSpace(draft.Text))
            {
                ResetDraft(draft, input, OrientationState(selection, axis), error);
                return;
            }
            if (!TryFiniteFloat(draft.Text, out float value))
            {
                draft.Error = true;
                error.Active = true;
                return;
            }
            ApplyOrientation(selection, axis, _ => value);
            ResetDraft(draft, input, OrientationState(selection, axis), error);
        }

        static void StepOrientation(Part[] selection, int axis, Draft draft, TextInput input, Label error, float step)
        {
            ++draft.Pending;
            if (!GUI.SelectionMatches(selection)) return;
            if (draft.Dirty)
            {
                if (!TryFiniteFloat(draft.Text, out float value) ||
                    float.IsNaN(value + step) || float.IsInfinity(value + step))
                {
                    draft.Error = true;
                    error.Active = true;
                    return;
                }
                ApplyOrientation(selection, axis, _ => value + step);
                ResetDraft(draft, input, OrientationState(selection, axis), error);
            }
            else
                ApplyOrientation(selection, axis, part => ReadOrientation(part, axis) + step);
        }

        static void ApplyOrientation(Part[] selection, int axis, Func<Part, float> nextValue)
        {
            if (!GUI.SelectionMatches(selection)) return;
            float[] values = selection.Select(nextValue).ToArray();
            if (values.Any(value => float.IsNaN(value) || float.IsInfinity(value)))
            {
                Message("Orientation exceeds the supported number range");
                return;
            }
            if (!selection.Where((part, index) => ReadOrientation(part, axis) != values[index]).Any())
                return;
            var modules = selection.Select(part => part.orientation).ToList();
            Undo.main.RecordStatChangeStep(modules, () =>
            {
                for (int i = 0; i < selection.Length; i++)
                {
                    Part part = selection[i];
                    Orientation old = part.orientation.orientation.Value;
                    Orientation updated = axis == 0 ? new Orientation(values[i], old.y, old.z) :
                        axis == 1 ? new Orientation(old.x, values[i], old.z) :
                        new Orientation(old.x, old.y, values[i]);
                    part.orientation.orientation.Value = updated;
                    part.RegenerateMesh();
                }
            });
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

        static void AddFieldRow(Box box, FieldGroup group, Part[] selection, int frameWidth, bool rawMode)
        {
            string label = FieldLabel(group, rawMode);
            string titled = $"{label} ({group.Parts.Count}/{selection.Length})";
            int rowWidth = frameWidth - 50;
            int labelWidth = Mathf.Clamp(Mathf.RoundToInt(rowWidth * 0.42f), 150, 200);
            int controlWidth = rowWidth - labelWidth - 8;
            if (group.Kind == FieldKind.Toggle)
            {
                ToggleWithLabel toggle = null;
                toggle = Builder.CreateToggleWithLabel(box, rowWidth, 52,
                    () => group.Parts.All(part => part.variablesModule.boolVariables.GetValue(group.Key)),
                    () =>
                    {
                        bool allOn = group.Parts.All(part => part.variablesModule.boolVariables.GetValue(group.Key));
                        ApplyToggle(group, selection, !allOn);
                        toggle.toggle.toggleButton.UpdateUI(false);
                    }, labelText: titled);
                toggle.label.AutoFontResize = false;
                toggle.label.FontSize = rawMode ? 19 : 22;
                bool? lastValue = null, lastMixed = null;
                refreshers.Add(() =>
                {
                    if (toggle.gameObject == null) return;
                    bool first = group.Parts[0].variablesModule.boolVariables.GetValue(group.Key);
                    bool mixed = group.Parts.Any(part => part.variablesModule.boolVariables.GetValue(group.Key) != first);
                    if (lastMixed != mixed)
                        toggle.label.Text = mixed ? $"{titled} · Mixed" : titled;
                    if (lastValue != first || lastMixed != mixed)
                        toggle.toggle.toggleButton.UpdateUI(false);
                    lastValue = first;
                    lastMixed = mixed;
                });
                refreshers[refreshers.Count - 1]();
                return;
            }
            if (group.Kind == FieldKind.Number &&
                group.Parts.Any(part => !IsFinite(part.variablesModule.doubleVariables.GetValue(group.Key))))
            {
                string saved = NumberState(group, true);
                AddLabel(box, rowWidth,
                    $"{titled}: {(saved == "" ? "Mixed" : saved)} · non-finite saved value", 35, 17);
                return;
            }
            if (rawMode)
                AddLabel(box, rowWidth, titled, 30, 19);
            Container row = Builder.CreateContainer(box);
            Object.Destroy(row.gameObject.GetComponent<ContentSizeFitter>());
            row.CreateLayoutGroup(Type.Horizontal, spacing: 8);
            row.Size = new Vector2(rowWidth, 52);
            LayoutElement rowLayout = row.gameObject.AddComponent<LayoutElement>();
            rowLayout.preferredWidth = rowWidth;
            rowLayout.preferredHeight = 52;
            Container info = Builder.CreateContainer(row);
            Object.Destroy(info.gameObject.GetComponent<ContentSizeFitter>());
            info.CreateLayoutGroup(Type.Vertical, spacing: 0);
            info.Size = new Vector2(labelWidth, 50);
            LayoutElement infoLayout = info.gameObject.AddComponent<LayoutElement>();
            infoLayout.preferredWidth = labelWidth;
            infoLayout.preferredHeight = 50;
            if (!rawMode)
                AddLabel(info, labelWidth, titled, 50, 21);
            Container control = Builder.CreateContainer(row);
            Object.Destroy(control.gameObject.GetComponent<ContentSizeFitter>());
            control.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            control.Size = new Vector2(controlWidth, 44);
            LayoutElement controlLayout = control.gameObject.AddComponent<LayoutElement>();
            controlLayout.preferredWidth = controlWidth;
            controlLayout.preferredHeight = 44;
            if (group.Kind == FieldKind.Number)
            {
                Draft draft = GetDraft(group, rawMode, NumberState(group, rawMode));
                double step = IsPercent(group) ? (rawMode ? 0.01 : 1) : Config.settings.numberChangeStep.Value;
                TextInput value = null;
                Label error = null;
                Builder.CreateButton(control, 32, 32,
                    onClick: () => HandleNumberStep(group, selection, rawMode, draft, value, error, -step), text: "<");
                value = Builder.CreateTextInput(control, controlWidth - 74, 44, text: draft.Text);
                SetPlaceholder(value, "Mixed");
                error = AddLabel(box, rowWidth, "Invalid number · not applied", 25, 14);
                error.Active = draft.Error;
                value.field.onValueChanged.AddListener(changed =>
                {
                    if (!draft.Dirty)
                        draft.NumberBaseline = group.Parts.Select(part => part.variablesModule.doubleVariables.GetValue(group.Key)).ToArray();
                    draft.Text = changed;
                    draft.Dirty = changed != draft.Rendered;
                    draft.Error = false;
                    error.Active = false;
                });
                int generation = selectionVersion;
                value.field.onEndEdit.AddListener(_ =>
                {
                    if (!rebuilding && draft.Dirty)
                        CommitNumberLater(group, selection, rawMode, draft, value, error, generation).Forget();
                });
                Builder.CreateButton(control, 32, 32,
                    onClick: () => HandleNumberStep(group, selection, rawMode, draft, value, error, step), text: ">");
                refreshers.Add(() =>
                {
                    if (value.gameObject == null) return;
                    if (draft.Dirty)
                    {
                        if (NumberBaselineChanged(group, draft))
                        {
                            ResetDraft(draft, value, NumberState(group, rawMode), error);
                            Message("Value changed outside the editor; draft cancelled");
                        }
                        return;
                    }
                    if (value.field.isFocused) return;
                    string current = NumberState(group, rawMode);
                    if (value.Text != current) value.field.SetTextWithoutNotify(current);
                    draft.Rendered = draft.Text = current;
                });
                return;
            }

            string displayedText = TextState(group);
            Draft textDraft = GetDraft(group, rawMode, displayedText);
            TextInput text = Builder.CreateTextInput(control, controlWidth - 70, 40, text: textDraft.Text);
            SetPlaceholder(text, TextMixed(group) ? "Mixed" : "Empty");
            Label textError = AddLabel(box, rowWidth, "Text was not applied", 25, 14);
            textError.Active = textDraft.Error;
            text.field.onValueChanged.AddListener(changed =>
            {
                if (!textDraft.Dirty)
                    textDraft.TextBaseline = group.Parts.Select(part => part.variablesModule.stringVariables.GetValue(group.Key)).ToArray();
                textDraft.Text = changed;
                textDraft.Dirty = changed != textDraft.Rendered;
                textDraft.Error = false;
                textError.Active = false;
            });
            int textGeneration = selectionVersion;
            text.field.onEndEdit.AddListener(_ =>
            {
                if (!rebuilding && textDraft.Dirty)
                    CommitTextLater(group, selection, textDraft, text, textError, textGeneration).Forget();
            });
            Builder.CreateButton(control, 65, 40,
                onClick: () =>
                {
                    ++textDraft.Pending;
                    if (GUI.SelectionMatches(selection))
                        SetText(group, selection, "");
                    ResetDraft(textDraft, text, TextState(group), textError);
                }, text: "Clear");
            refreshers.Add(() =>
            {
                if (text.gameObject == null) return;
                if (textDraft.Dirty)
                {
                    if (TextBaselineChanged(group, textDraft))
                    {
                        ResetDraft(textDraft, text, TextState(group), textError);
                        Message("Text changed outside the editor; draft cancelled");
                    }
                    return;
                }
                if (text.field.isFocused) return;
                string current = TextState(group);
                if (text.Text != current) text.field.SetTextWithoutNotify(current);
                textDraft.Rendered = textDraft.Text = current;
                SetPlaceholder(text, TextMixed(group) ? "Mixed" : "Empty");
            });
        }

        static void SetPlaceholder(TextInput input, string text)
        {
            if (input.field?.placeholder is TMP_Text placeholder)
                placeholder.text = text;
        }

        static void ResetDraft(Draft draft, TextInput input, string current, Label error)
        {
            draft.Pending++;
            draft.Rendered = draft.Text = current;
            draft.Dirty = draft.Error = false;
            draft.NumberBaseline = null;
            draft.TextBaseline = null;
            if (input.gameObject != null)
                input.field.SetTextWithoutNotify(current);
            if (error.gameObject != null)
                error.Active = false;
        }

        static bool NumberBaselineChanged(FieldGroup group, Draft draft) => draft.NumberBaseline != null &&
            (draft.NumberBaseline.Length != group.Parts.Count || group.Parts.Where((part, i) =>
                part.variablesModule.doubleVariables.GetValue(group.Key) != draft.NumberBaseline[i]).Any());

        static bool TextBaselineChanged(FieldGroup group, Draft draft) => draft.TextBaseline != null &&
            (draft.TextBaseline.Length != group.Parts.Count || group.Parts.Where((part, i) =>
                !string.Equals(part.variablesModule.stringVariables.GetValue(group.Key),
                    draft.TextBaseline[i], StringComparison.Ordinal)).Any());

        static async UniTaskVoid CommitNumberLater(FieldGroup group, Part[] selection, bool rawMode,
            Draft draft, TextInput input, Label error, int generation)
        {
            int pending = ++draft.Pending;
            if (Input.GetMouseButton(0))
                await UniTask.WaitUntil(() => !Input.GetMouseButton(0));
            await UniTask.Yield();
            await UniTask.Yield();
            if (draft.Pending != pending || selectionVersion != generation || rebuilding ||
                input.gameObject == null || !GUI.SelectionMatches(selection) || !draft.Dirty)
                return;
            if (input.field.wasCanceled || string.IsNullOrWhiteSpace(draft.Text))
            {
                ResetDraft(draft, input, NumberState(group, rawMode), error);
                return;
            }
            if (!TryNumber(group, rawMode, draft.Text, out double value))
            {
                draft.Error = true;
                error.Active = true;
                Message(IsPercent(group) ? "Percentage must be between 0 and 100" : "Enter a valid number");
                return;
            }
            SetNumberAbsolute(group, selection, rawMode, value);
            ResetDraft(draft, input, NumberState(group, rawMode), error);
        }

        static async UniTaskVoid CommitTextLater(FieldGroup group, Part[] selection,
            Draft draft, TextInput input, Label error, int generation)
        {
            int pending = ++draft.Pending;
            if (Input.GetMouseButton(0))
                await UniTask.WaitUntil(() => !Input.GetMouseButton(0));
            await UniTask.Yield();
            await UniTask.Yield();
            if (draft.Pending != pending || selectionVersion != generation || rebuilding ||
                input.gameObject == null || !GUI.SelectionMatches(selection) || !draft.Dirty)
                return;
            if (!input.field.wasCanceled && !(TextMixed(group) && draft.Text == ""))
                SetText(group, selection, draft.Text);
            ResetDraft(draft, input, TextState(group), error);
        }

        static void HandleNumberStep(FieldGroup group, Part[] selection, bool rawMode,
            Draft draft, TextInput input, Label error, double step)
        {
            ++draft.Pending;
            if (!GUI.SelectionMatches(selection))
                return;
            if (draft.Dirty)
            {
                if (!TryNumber(group, rawMode, draft.Text, out double value))
                {
                    draft.Error = true;
                    error.Active = true;
                    Message("Finish or cancel the invalid number first");
                    return;
                }
                double next = value + step;
                if (IsPercent(group))
                    next = Math.Max(0, Math.Min(rawMode ? 1 : 100, next));
                if (!double.IsNaN(next) && !double.IsInfinity(next))
                    SetNumberAbsolute(group, selection, rawMode, next);
                ResetDraft(draft, input, NumberState(group, rawMode), error);
            }
            else
                NudgeNumber(group, selection, step, rawMode);
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

        static Label AddLabel(Transform parent, int width, string text, int height = 35, int fontSize = 17)
        {
            Label label = Builder.CreateLabel(parent, width, height, text: text);
            label.AutoFontResize = false;
            label.FontSize = fontSize;
            label.TextAlignment = TextAlignmentOptions.MidlineLeft;
            return label;
        }

        static bool IsFeatured(FieldGroup group) => IsPercent(group) ||
            group.Kind == FieldKind.Toggle &&
            (group.Key == "engine_on" || group.Key == "gimbal_on" || group.Key == "heat_on__for_creative_use");

        static string NumberState(FieldGroup group, bool rawMode)
        {
            double first = ReadDisplayNumber(group, group.Parts[0], rawMode);
            return group.Parts.Any(part => ReadDisplayNumber(group, part, rawMode) != first)
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

        static string FieldLabel(FieldGroup group, bool rawMode)
        {
            if (rawMode)
                return $"{group.Key} [{group.Kind.ToString().ToLowerInvariant()}, raw]";
            if (group.Kind == FieldKind.Number && IsPercent(group))
                return PartFieldSemantics.PercentLabel(group.Key);
            if (group.Kind == FieldKind.Toggle)
                return group.Key switch
                {
                    "engine_on" => "Engine",
                    "gimbal_on" => "Gimbal",
                    "heat_on__for_creative_use" => "Heat (creative)",
                    _ => group.Key
                };
            return group.Key;
        }

        static bool IsPercent(FieldGroup group) => group.Kind == FieldKind.Number &&
                                                    PartFieldSemantics.IsPercent(group.Key);

        static double ReadDisplayNumber(FieldGroup group, Part part, bool rawMode)
        {
            double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
            return IsPercent(group) && !rawMode ? raw * 100 : raw;
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
            if (selection.Any(part => float.IsNaN(part.Position.x + offset.x) ||
                                      float.IsInfinity(part.Position.x + offset.x) ||
                                      float.IsNaN(part.Position.y + offset.y) ||
                                      float.IsInfinity(part.Position.y + offset.y)))
            {
                Message("Move would exceed the supported position range");
                return;
            }
            Undo.main.RecordStatChangeStep(selection, () =>
            {
                foreach (Part part in selection)
                    part.Position += offset;
            });
        }

        static bool TryNumber(FieldGroup group, bool rawMode, string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                   !double.IsNaN(value) && !double.IsInfinity(value) &&
                   (!IsPercent(group) || value >= 0 && value <= (rawMode ? 1 : 100));
        }

        static void SetNumberAbsolute(FieldGroup group, Part[] selection, bool rawMode, double value)
        {
            if (!GUI.SelectionMatches(selection))
                return;
            double raw = IsPercent(group) && !rawMode ? value / 100 : value;
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

        static void NudgeNumber(FieldGroup group, Part[] selection, double step, bool rawMode)
        {
            if (!GUI.SelectionMatches(selection) || step == 0)
                return;
            Part[] targets = NumberTargets(group);
            if (targets.Length == 0)
                return;
            double change = IsPercent(group) && !rawMode ? step / 100 : step;
            if (targets.All(part =>
            {
                double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
                double next = IsPercent(group) ? Math.Max(0, Math.Min(1, raw + change)) : raw + change;
                return double.IsNaN(next) || double.IsInfinity(next) || next == raw;
            }))
                return;
            Undo.main.RecordStatChangeStep(targets, () =>
            {
                foreach (Part part in targets)
                {
                    double raw = part.variablesModule.doubleVariables.GetValue(group.Key);
                    double next = IsPercent(group) ? Math.Max(0, Math.Min(1, raw + change)) : raw + change;
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

        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        static void Message(string text) => SFS.UI.MsgDrawer.main.Log(text);
    }
}
