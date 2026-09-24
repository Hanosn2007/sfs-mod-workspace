using System.Collections.Generic;
using SFS.UI.ModGUI;
using UITools;
using UnityEngine;
using UnityEngine.UI;
using Button = SFS.UI.ModGUI.Button;
using Type = SFS.UI.ModGUI.Type;

namespace BuildTools
{
    // Keeps the existing editors and their settings, but presents one page at a time.
    internal static class TabbedPanel
    {
        internal enum Page { Part, Build, Json }

        private static Window partWindow;
        private static Window buildWindow;
        private static Window jsonWindow;
        private static Window activeWindow;
        private static Vector2 frameSize;
        private static readonly Dictionary<Window, RectTransform> tabBars = new();
        private static readonly Dictionary<Window, List<Button>> tabButtons = new();
        private const string PositionX = "BuildTools.TabbedPanel.X";
        private const string PositionY = "BuildTools.TabbedPanel.Y";

        internal static Vector2 FrameSize => frameSize == Vector2.zero
            ? new Vector2(Mathf.Max(400, PartText.Settings.settings.windowSize.x),
                Mathf.Max(450, PartText.Settings.settings.windowSize.y))
            : frameSize;

        internal static void AddTabs(Window window, int width, Page current, bool hasJson)
        {
            if (tabBars.ContainsKey(window))
                return;

            // Keep navigation in the window frame, outside the Part page's scroll content.
            Container tabs = Builder.CreateContainer(window.rectTransform);
            tabs.gameObject.name = "BuildTools Fixed Tabs";
            UnityEngine.Object.Destroy(tabs.gameObject.GetComponent<ContentSizeFitter>());
            tabs.CreateLayoutGroup(Type.Horizontal, spacing: 5);
            RectTransform rect = tabs.gameObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1);
            rect.pivot = new Vector2(0.5f, 1);
            rect.anchoredPosition = new Vector2(0, -48);
            rect.sizeDelta = new Vector2(width - 20, 34);
            tabs.gameObject.transform.SetAsLastSibling();
            tabBars[window] = rect;

            var contentLayout = window.ChildrenHolder.GetComponent<VerticalLayoutGroup>();
            if (contentLayout != null)
            {
                RectOffset old = contentLayout.padding;
                contentLayout.padding = new RectOffset(old.left, old.right, 50, 50);
            }

            int count = hasJson ? 3 : 2;
            int buttonWidth = (width - 20 - (count - 1) * 5) / count;
            var buttons = new List<Button>
            {
                AddTab(tabs, buttonWidth, Page.Part, current, "Part"),
                AddTab(tabs, buttonWidth, Page.Build, current, "Build")
            };
            if (hasJson)
                buttons.Add(AddTab(tabs, buttonWidth, Page.Json, current, "JSON"));
            tabButtons[window] = buttons;
        }

        private static Button AddTab(Transform parent, int width, Page page, Page current, string title)
        {
            Button button = Builder.CreateButton(parent, width, 34, onClick: () => Show(page), text: title);
            if (page != current)
                button.TextOpacity = 0.65f;
            return button;
        }

        internal static void Initialize(Window part, Window build, Window json)
        {
            partWindow = part;
            buildWindow = build;
            jsonWindow = json;
            activeWindow = null;
            frameSize = FrameSize;
            Normalize(part);
            Normalize(build);
            Normalize(json);
            RegisterMinimize(part);
            RegisterMinimize(build);
            RegisterMinimize(json);
            AddResizeHandle(part);
            AddResizeHandle(build);
            AddResizeHandle(json);

            // The JSON window was already placed beside the build canvas; use its saved position.
            Vector3 position = json != null ? json.rectTransform.position : part.rectTransform.position;
            if (PlayerPrefs.HasKey(PositionX) && PlayerPrefs.HasKey(PositionY))
            {
                position.x = PlayerPrefs.GetFloat(PositionX) * Screen.width;
                position.y = PlayerPrefs.GetFloat(PositionY) * Screen.height;
            }
            part.RegisterOnDropListener(SavePosition);
            build.RegisterOnDropListener(SavePosition);
            json?.RegisterOnDropListener(SavePosition);
            Show(Page.Part, position);
        }

        internal static void Show(Page page)
        {
            if (partWindow == null || partWindow.gameObject == null)
                return;
            Vector3 position = activeWindow != null && activeWindow.gameObject != null
                ? activeWindow.rectTransform.position
                : partWindow.rectTransform.position;
            Show(page, position);
        }

        private static void Show(Page page, Vector3 position)
        {
            Window next = page switch
            {
                Page.Part => partWindow,
                Page.Build => buildWindow,
                Page.Json => jsonWindow,
                _ => null
            };
            if (next == null || next.gameObject == null || next == activeWindow)
                return;

            if (activeWindow == jsonWindow && PartText.UI.input?.field != null)
            {
                PartText.UI.input.field.DeactivateInputField();
                PartText.UI.editingText = false;
            }

            if (partWindow != null) partWindow.Active = false;
            if (buildWindow != null) buildWindow.Active = false;
            if (jsonWindow != null) jsonWindow.Active = false;

            next.rectTransform.position = position;
            Normalize(next);
            next.Active = true;
            KeepInView(next.rectTransform);
            activeWindow = next;
        }

        internal static void SetFrameSize(Vector2Int size)
        {
            frameSize = new Vector2(Mathf.Max(400, size.x), Mathf.Max(450, size.y));
            PartText.Settings.settings.windowSize = frameSize;
            Normalize(partWindow);
            Normalize(buildWindow);
            Normalize(jsonWindow);
            PartText.UI.ResizeTo(frameSize);
            if (buildWindow?.ChildrenHolder is RectTransform buildContent)
                LayoutRebuilder.ForceRebuildLayoutImmediate(buildContent);
            foreach (var pair in tabBars)
                if (pair.Key != null && pair.Key.gameObject != null && pair.Value != null)
                    pair.Value.sizeDelta = new Vector2(frameSize.x - 20, 34);
            foreach (var pair in tabButtons)
            {
                int count = pair.Value.Count;
                int width = Mathf.FloorToInt((frameSize.x - 20 - (count - 1) * 5) / count);
                foreach (Button button in pair.Value)
                    if (button != null && button.gameObject != null)
                        button.Size = new Vector2(width, 34);
            }
        }

        private static void AddResizeHandle(Window window)
        {
            if (window == null)
                return;
            Button handle = Builder.CreateButton(window.rectTransform, 36, 36,
                onClick: () => { }, text: "//");
            handle.gameObject.name = "BuildTools Resize Corner";
            RectTransform rect = handle.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(1, 0);
            rect.pivot = new Vector2(1, 0);
            rect.anchoredPosition = new Vector2(-5, 5);
            rect.sizeDelta = new Vector2(36, 36);
            handle.gameObject.AddComponent<PointerResizeHandle>().Initialize(window, rect);
            handle.gameObject.transform.SetAsLastSibling();
            if (window is ClosableWindow closable)
            {
                handle.Active = !closable.Minimized;
                closable.OnMinimizedChangedEvent += () => handle.Active = !closable.Minimized;
            }
        }

        internal static bool IsActiveWindow(Window window) => window == activeWindow;

        internal static void ResizeFromPointerDelta(Vector2 screenDelta)
        {
            if (activeWindow == null || activeWindow.gameObject == null)
                return;
            Vector3 scale = activeWindow.rectTransform.lossyScale;
            if (Mathf.Abs(scale.x) < 0.001f || Mathf.Abs(scale.y) < 0.001f)
                return;
            Vector2Int requested = Vector2Int.RoundToInt(frameSize +
                new Vector2(screenDelta.x / scale.x, -screenDelta.y / scale.y));
            requested.x = Mathf.Clamp(requested.x, 400, Mathf.FloorToInt(Screen.width / scale.x) - 20);
            requested.y = Mathf.Clamp(requested.y, 450, Mathf.FloorToInt(Screen.height / scale.y) - 20);
            if (requested == Vector2Int.RoundToInt(frameSize))
                return;
            Vector3[] corners = new Vector3[4];
            activeWindow.rectTransform.GetWorldCorners(corners);
            Vector3 topLeft = corners[1];
            SetFrameSize(requested);
            activeWindow.rectTransform.GetWorldCorners(corners);
            activeWindow.rectTransform.position += topLeft - corners[1];
            KeepInView(activeWindow.rectTransform);
        }

        internal static void FinishResize()
        {
            Vector2Int size = Vector2Int.RoundToInt(frameSize);
            PartEditor.Config.settings.windowSize.Value = size;
            PartEditor.Config.settings.stretchToFit.Value = false;
            PartText.Settings.Save?.Invoke();
            SavePosition();
        }

        internal static void SetScale(float scale)
        {
            Scale(partWindow, scale);
            Scale(buildWindow, scale);
            Scale(jsonWindow, scale);
        }

        private static void Scale(Window window, float scale)
        {
            if (window != null && window.gameObject != null)
                window.gameObject.transform.localScale = new Vector3(scale, scale, 1);
        }

        private static void Normalize(Window window)
        {
            if (window == null || window.gameObject == null)
                return;
            window.Size = FrameSize;
            Scale(window, BuildSettings.Config.settings.windowScale.Value);
            if (window is ClosableWindow closable)
                closable.Minimized = PartText.Settings.settings.windowMinimized;
        }

        private static void RegisterMinimize(Window window)
        {
            if (window is ClosableWindow closable)
            {
                void UpdateMinimized()
                {
                    PartText.Settings.settings.windowMinimized = closable.Minimized;
                    if (tabBars.TryGetValue(window, out RectTransform tabs) && tabs != null)
                        tabs.gameObject.SetActive(!closable.Minimized);
                }
                closable.OnMinimizedChangedEvent += UpdateMinimized;
                UpdateMinimized();
            }
        }

        private static void KeepInView(RectTransform rect)
        {
            Vector3[] corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            Vector3 offset = Vector3.zero;
            if (corners[0].x < 0) offset.x = -corners[0].x;
            if (corners[2].x + offset.x > Screen.width)
                offset.x = Screen.width - corners[2].x;
            if (corners[0].y < 0) offset.y = -corners[0].y;
            if (corners[2].y + offset.y > Screen.height)
                offset.y = Screen.height - corners[2].y;
            rect.position += offset;
        }

        private static void SavePosition()
        {
            if (activeWindow == null || activeWindow.gameObject == null)
                return;
            KeepInView(activeWindow.rectTransform);
            Vector3 position = activeWindow.rectTransform.position;
            PlayerPrefs.SetFloat(PositionX, position.x / Screen.width);
            PlayerPrefs.SetFloat(PositionY, position.y / Screen.height);
            PlayerPrefs.Save();
        }

        internal static void OnBuildUnloaded()
        {
            partWindow = null;
            buildWindow = null;
            jsonWindow = null;
            activeWindow = null;
            frameSize = Vector2.zero;
            tabBars.Clear();
            tabButtons.Clear();
        }
    }

    // The game's ModGUI buttons receive clicks, but Unity's normal drag event is not delivered
    // to a child of this draggable window. Poll the pointer only while it is held on the corner.
    internal sealed class PointerResizeHandle : MonoBehaviour
    {
        private Window window;
        private RectTransform hitRect;
        private bool dragging;
        private Vector2 previousPointer;

        internal void Initialize(Window targetWindow, RectTransform targetRect)
        {
            window = targetWindow;
            hitRect = targetRect;
        }

        private void Update()
        {
            if (!TabbedPanel.IsActiveWindow(window))
                return;
            Vector2 pointer = Input.mousePosition;
            if (!dragging && Input.GetMouseButtonDown(0) &&
                RectTransformUtility.RectangleContainsScreenPoint(hitRect, pointer, null))
            {
                dragging = true;
                previousPointer = pointer;
            }
            if (!dragging)
                return;
            if (Input.GetMouseButton(0))
            {
                TabbedPanel.ResizeFromPointerDelta(pointer - previousPointer);
                previousPointer = pointer;
            }
            if (Input.GetMouseButtonUp(0))
            {
                dragging = false;
                TabbedPanel.FinishResize();
            }
        }
    }
}
