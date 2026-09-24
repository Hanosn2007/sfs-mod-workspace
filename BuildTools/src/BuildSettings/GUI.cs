using System.Globalization;
using JetBrains.Annotations;
using SFS.Builds;
using SFS.UI.ModGUI;
using UITools;
using UnityEngine;
using UnityEngine.UI;
using static SFS.UI.ModGUI.Builder;
using static UITools.ModSettings<BuildSettings.Config.SettingsData>;
using GUIElement = SFS.UI.ModGUI.GUIElement;

namespace BuildSettings
{
    public class NumberInput
    {
        public TextInput textInput;
        public string oldText;
        public double defaultVal;
        public double currentVal;
        public double min;
        public double max;
    }

    [UsedImplicitly]
    public class GUI
    {
        public static GameObject windowHolder;
        private static Vector2 gameSize;
        public static GUI inst;

        private static readonly int MainWindowID = GetRandomID();
        private static ClosableWindow window;
        public static Window Window => window;

        private static ToggleWithLabel snapToggle;
        private static ToggleWithLabel adaptToggle;
        private static ToggleWithLabel invertKeyToggle;
        public static NumberInput gridSnapData;
        private static NumberInput rotationData;

        public static bool snapping = true;
        public static bool adapting = true;
        public static bool invertKeys;


        public static bool noAdaptOverride;

        public static void Setup()
        {
            gridSnapData = CreateData(settings.defaultGridSnap, 0.000000000000000000001, 99999);
            rotationData = CreateData(settings.defaultRotateDegrees, 0.000000000000000000001, 99999);

            ShowGUI();
            windowHolder.AddComponent<NativeRotationFallback>();
            window.RegisterOnDropListener(OnDragDrop);
            ClampWindow(window);
            Defaults();
            settings.windowScale.OnChange += Scale;

            BuildManager.main.buildCamera.maxCameraDistance = 300;
            BuildManager.main.buildCamera.minCameraDistance = 0.1f;
        }

        private static NumberInput CreateData(double defaultVal, double min, double max)
        {
            var ToReturn = new NumberInput
            {
                textInput = new TextInput(),
                oldText = defaultVal.ToString(CultureInfo.InvariantCulture),
                defaultVal = defaultVal,
                currentVal = defaultVal,
                min = min,
                max = max
            };
            return ToReturn;
        }

        private static void ShowGUI()
        {

            windowHolder = CreateHolder(SceneToAttach.CurrentScene, "Build Settings");

            window = UIToolsBuilder.CreateClosableWindow(windowHolder.transform, MainWindowID, 400, 600,
                (int)(gameSize.x / 2) - 500, (int)(gameSize.y / 2) - 300, true, false, 0.95f, "Build Tools");

            window.RegisterPermanentSaving("BuildSettings.windowPosition");

            window.CreateLayoutGroup(Type.Vertical);
            window.EnableScrolling(Type.Vertical);
            BuildTools.TabbedPanel.AddTabs(window, 400, BuildTools.TabbedPanel.Page.Build,
                PartText.Settings.settings.windowEnabled);
            if (window.ChildrenHolder.GetComponent<VerticalLayoutGroup>() is { } contentLayout)
                contentLayout.childAlignment = TextAnchor.MiddleCenter;
            snapToggle = CreateToggleWithLabel(window, 320, 35, () => snapping, () => snapping = !snapping, 0, 0, "Snap to Parts");
            adaptToggle = CreateToggleWithLabel(window, 320, 35, () => adapting, () => adapting = !adapting, 0, 0, "Part Adaptation");
            invertKeyToggle = CreateToggleWithLabel(window, 320, 35, () => invertKeys, () => invertKeys = !invertKeys, 0, 0, "Invert Rotate Keybinds");

            Box box = CreateBox(window, 355, 140, 0, 0, 0.75f);
            box.CreateLayoutGroup(Type.Vertical, spacing: 10f);

            Container gridSnapContainer = CreateContainer(box);
            gridSnapContainer.CreateLayoutGroup(Type.Horizontal, spacing: 10f);

            CreateLabel(gridSnapContainer, 200, 35, 0, 0, "Grid Snap");
            CreateSpace(gridSnapContainer, 20, 0);
            gridSnapData.textInput = CreateTextInput(gridSnapContainer, 90, 50, 0, 0, gridSnapData.defaultVal.ToString(CultureInfo.InvariantCulture), MakeNumber);

            Container rotationContainer = CreateContainer(box);
            rotationContainer.CreateLayoutGroup(Type.Horizontal, spacing: 10f);
            CreateLabel(rotationContainer, 200, 35, 0, 0, "Rotation Degrees");
            CreateSpace(rotationContainer, 20, 0);
            rotationData.textInput = CreateTextInput(rotationContainer, 90, 50, 0, 0, rotationData.defaultVal.ToString(CultureInfo.InvariantCulture), MakeNumber);

            Container buttonsContainer = CreateContainer(window);
            buttonsContainer.CreateLayoutGroup(Type.Horizontal, spacing: 5f);
            CreateButton(buttonsContainer, 140, 40, 0, 0, Defaults, "Defaults");
            CreateButton(buttonsContainer, 90, 40, 0, 0, Save, "Save");
            CreateButton(buttonsContainer, 90, 40, 0, 0, Reset, "Reset");

            window.gameObject.transform.localScale = new Vector3(settings.windowScale.Value, settings.windowScale.Value, 1f);
        }

        private static void Reset()
        {
            settings.adaptingByDefault = true;
            settings.snappingByDefault = true;
            settings.invertKeysByDefault = false;
            settings.defaultGridSnap = gridSnapData.defaultVal = 0.5;
            settings.defaultRotateDegrees = rotationData.defaultVal = 90;
            Defaults();
            Config.SaveSettings?.Invoke();
        }

        private static void Save()
        {
            settings.adaptingByDefault = adapting;
            settings.snappingByDefault = snapping;
            settings.invertKeysByDefault = invertKeys;
            
            settings.defaultGridSnap = gridSnapData.defaultVal = gridSnapData.currentVal;
            settings.defaultRotateDegrees = rotationData.defaultVal = rotationData.currentVal;
            Config.SaveSettings?.Invoke();
        }
        
        private static void Defaults()
        {
            snapping = settings.snappingByDefault;
            adapting = settings.adaptingByDefault;
            invertKeys = settings.invertKeysByDefault;
            snapToggle.toggle.toggleButton.UpdateUI(false);
            adaptToggle.toggle.toggleButton.UpdateUI(false);
            invertKeyToggle.toggle.toggleButton.UpdateUI(false);
            gridSnapData.currentVal = gridSnapData.defaultVal;
            gridSnapData.textInput.Text = gridSnapData.defaultVal.ToString(CultureInfo.InvariantCulture);
            rotationData.currentVal = rotationData.defaultVal;
            rotationData.textInput.Text = rotationData.defaultVal.ToString(CultureInfo.InvariantCulture);
            PartModifiers.modifierToggle = false;
            PartModifiers.orientationToggle = false;
        }

        private static void MakeNumber(string text)
        {
            gridSnapData = Numberify(gridSnapData);
            rotationData = Numberify(rotationData);
        }

        private static void Scale()
        {
            BuildTools.TabbedPanel.SetScale(settings.windowScale.Value);
            ClampWindow(window);
        }

        private static NumberInput Numberify(NumberInput data)
        {
            string text = data.textInput.Text;
            if (text is "." or "" or "-")
                return data;

            if (text.Length > 20)
            {
                data.textInput.Text = data.oldText;
                return data;
            }

            double numCheck;
            try
            {
                numCheck = double.Parse(text, CultureInfo.InvariantCulture);
            }
            catch
            {
                data.textInput.Text = data.oldText;
                return data;
            }

            if (double.IsNaN(numCheck) || double.IsInfinity(numCheck) ||
                numCheck == 0 || numCheck < data.min || numCheck > data.max)
            {
                data.currentVal = data.defaultVal;
                data.textInput.Text = data.defaultVal.ToString(CultureInfo.InvariantCulture);
            }
            else
                data.currentVal = numCheck.Round(0.000000000000000000001);

            data.oldText = data.textInput.Text;
            return data;
        }


        private static void ClampWindow(GUIElement input)
        {
            gameSize = new Vector2(windowHolder.GetComponentInParent<CanvasScaler>().referenceResolution.y / Screen.height * Screen.width, windowHolder.GetComponentInParent<CanvasScaler>().referenceResolution.y);

            Vector2 pos = input.Position;
            pos.x = Mathf.Clamp(pos.x, -(gameSize.x / 2) + (settings.windowScale.Value * window.Size.x / 2), (gameSize.x / 2) - (settings.windowScale.Value * window.Size.x / 2));
            pos.y = Mathf.Clamp(pos.y, -(gameSize.y / 2) + (window.Size.y * settings.windowScale.Value), gameSize.y / 2);
            input.Position = pos;
        }


        private static void OnDragDrop()
        {
            if (windowHolder == null) return;
            ClampWindow(window);
        }

        public static float GetRotationValue(bool useCustom, bool negative = false)
        {
            float value = 90;

            if (useCustom)
                value = (float)rotationData.currentVal;

            return negative ? -value : value;
        }

        public static void CustomRotate(bool inverse = false)
        {
            if (CustomRotation.PatchActive)
                CustomRotation.CustomListener = true;

            float amount = GetRotationValue(invertKeys, !inverse);
            BuildManager.main.buildMenus.Rotate(amount);
        }
    }
}
