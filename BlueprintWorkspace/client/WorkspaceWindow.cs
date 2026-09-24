using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SFS.Input;
using SFS.UI;
using UnityEngine;

namespace BlueprintWorkspace
{
    internal sealed class WorkspaceWindow : MonoBehaviour
    {
        private readonly ConcurrentQueue<Action> pending = new ConcurrentQueue<Action>();
        private List<LocalBlueprint> local = new List<LocalBlueprint>();
        private BlueprintSummary[] shared = Array.Empty<BlueprintSummary>();
        private WorkspaceMembership[] workspaces = Array.Empty<WorkspaceMembership>();
        private WorkspaceConfig config = new WorkspaceConfig();
        private string modFolder;
        private string invite = "";
        private string username = "";
        private string password = "";
        private string status = "Save a blueprint in game, then publish it here.";
        private bool inBuildScene;
        private bool busy;
        private int tab;
        private int authTab;
        private Vector2 scroll;
        private Vector2 workspaceScroll;
        private WorkspaceScreen workspaceScreen;
        private RectTransform newButtonAnchor;
        private GUIStyle panelStyle;
        private GUIStyle titleStyle;
        private GUIStyle bodyStyle;
        private GUIStyle mutedStyle;
        private GUIStyle fieldStyle;
        private GUIStyle buttonStyle;
        private GUIStyle activeButtonStyle;
        private GUIStyle rowStyle;
        private Rect windowRect;
        private Texture2D panelTexture;
        private Texture2D fieldTexture;
        private Texture2D buttonTexture;
        private Texture2D activeTexture;
        private Texture2D rowTexture;
        private float UiScale => Mathf.Clamp(Screen.height / 1080f, 1f, 2f);

        internal void Initialize(string folder)
        {
            modFolder = folder;
            try
            {
                string path = Path.Combine(modFolder, "WorkspaceConfig.json");
                if (File.Exists(path)) config = JsonConvert.DeserializeObject<WorkspaceConfig>(File.ReadAllText(path)) ?? new WorkspaceConfig();
                username = config.Username ?? "";
            }
            catch (Exception e) { status = "Could not read workspace settings: " + e.Message; }
        }

        internal void OnBuildLoaded()
        {
            inBuildScene = true;
            windowRect = new Rect();
            newButtonAnchor = null;
            RefreshLocal();
            if (!string.IsNullOrEmpty(config.Token)) RefreshWorkspaces();
        }

        internal void OnBuildUnloaded()
        {
            inBuildScene = false;
            CloseWorkspace();
            newButtonAnchor = null;
        }

        private void Update()
        {
            while (pending.TryDequeue(out Action action)) action();
            if (!inBuildScene) return;
            if (newButtonAnchor == null && Time.frameCount % 30 == 0) FindToolbarAnchor();
            if (Input.GetKeyDown(KeyCode.F8)) ToggleWorkspace();
        }

        private void OnGUI()
        {
            if (!inBuildScene || ScreenManager.main == null) return;
            EnsureStyles();
            Matrix4x4 previousMatrix = UnityEngine.GUI.matrix;
            float scale = UiScale;
            try
            {
                UnityEngine.GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
                float width = Screen.width / scale;
                float height = Screen.height / scale;
                bool showingWorkspace = workspaceScreen != null && ScreenManager.main.CurrentScreen == workspaceScreen;
                if (!showingWorkspace)
                {
                    if (ScreenManager.main.CurrentScreen is Screen_Game &&
                        UnityEngine.GUI.Button(GetToolbarRect(width, height, scale), "Workspace", buttonStyle))
                        ToggleWorkspace();
                    return;
                }
                float panelWidth = Mathf.Min(680, width - 32);
                float panelHeight = Mathf.Min(600, height - 32);
                if (windowRect.width != panelWidth || windowRect.height != panelHeight)
                    windowRect = new Rect(Mathf.Min(85, width - panelWidth - 8), Mathf.Min(85, height - panelHeight - 8), panelWidth, panelHeight);
                windowRect = UnityEngine.GUI.Window(177839, windowRect, DrawWindow, GUIContent.none, panelStyle);
                windowRect.x = Mathf.Clamp(windowRect.x, 0, width - windowRect.width);
                windowRect.y = Mathf.Clamp(windowRect.y, 0, height - windowRect.height);
            }
            finally { UnityEngine.GUI.matrix = previousMatrix; }
        }

        private void FindToolbarAnchor()
        {
            BuildMenuBar menuBar = FindFirstObjectByType<BuildMenuBar>();
            ButtonPC source = menuBar != null ? menuBar.newButton : null;
            if (source == null)
            {
                foreach (ButtonPC candidate in Resources.FindObjectsOfTypeAll<ButtonPC>())
                {
                    if (candidate == null || !candidate.gameObject.activeInHierarchy || candidate.ButtonText == null) continue;
                    if (string.Equals(candidate.ButtonText.text.Trim(), "New", StringComparison.OrdinalIgnoreCase))
                    {
                        source = candidate;
                        break;
                    }
                }
            }
            if (source != null) newButtonAnchor = source.GetComponent<RectTransform>();
        }

        private Rect GetToolbarRect(float width, float height, float scale)
        {
            Rect result = new Rect(Mathf.Max(8, width - 625), 8, 140, 35);
            if (newButtonAnchor == null) return result;
            Canvas canvas = newButtonAnchor.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector3[] corners = new Vector3[4];
            newButtonAnchor.GetWorldCorners(corners);
            Vector2 lowerLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 upperRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            result.x = Mathf.Clamp(lowerLeft.x / scale - result.width - 10, 8, width - result.width - 8);
            result.y = Mathf.Clamp((Screen.height - upperRight.y) / scale, 4, height - result.height - 4);
            return result;
        }

        private void ToggleWorkspace()
        {
            if (workspaceScreen != null && ScreenManager.main != null && ScreenManager.main.CurrentScreen == workspaceScreen)
            {
                CloseWorkspace();
                return;
            }
            if (!inBuildScene || ScreenManager.main == null || !(ScreenManager.main.CurrentScreen is Screen_Game)) return;
            if (workspaceScreen == null) workspaceScreen = gameObject.AddComponent<WorkspaceScreen>();
            ScreenManager.main.OpenScreen(() => workspaceScreen);
        }

        private void CloseWorkspace()
        {
            if (workspaceScreen != null && ScreenManager.main != null && ScreenManager.main.CurrentScreen == workspaceScreen)
                ScreenManager.main.CloseCurrent();
        }

        private void DrawWindow(int id)
        {
            GUILayout.BeginArea(new Rect(24, 18, windowRect.width - 48, windowRect.height - 100));
            GUILayout.BeginHorizontal();
            GUILayout.Label("Shared Blueprints", titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close  ×", buttonStyle, GUILayout.Width(100), GUILayout.Height(34))) CloseWorkspace();
            GUILayout.EndHorizontal();
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            if (string.IsNullOrEmpty(config.Token)) DrawJoin();
            else DrawLibrary();
            GUILayout.EndVertical();
            GUILayout.EndArea();
            UnityEngine.GUI.Label(new Rect(24, windowRect.height - 68, windowRect.width - 48, 54), status, mutedStyle);
            // Drag only the title region. Screen_Menu has already removed the
            // build screen from touch targets, so this cannot move the rocket.
            UnityEngine.GUI.DragWindow(new Rect(0, 0, windowRect.width - 135, 64));
        }

        private void DrawJoin()
        {
            GUILayout.Label("ACCOUNT", mutedStyle);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Sign in", authTab == 0 ? activeButtonStyle : buttonStyle, GUILayout.Height(38))) authTab = 0;
            if (GUILayout.Button("Create account", authTab == 1 ? activeButtonStyle : buttonStyle, GUILayout.Height(38))) authTab = 1;
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.Label("Server URL (HTTPS)", bodyStyle);
            config.Url = GUILayout.TextField(config.Url ?? "", fieldStyle, GUILayout.Height(38));
            GUILayout.Label("Username", bodyStyle);
            username = GUILayout.TextField(username ?? "", fieldStyle, GUILayout.Height(38));
            if (authTab == 1)
            {
                GUILayout.Label("Display name", bodyStyle);
                config.DisplayName = GUILayout.TextField(config.DisplayName ?? "", fieldStyle, GUILayout.Height(38));
            }
            GUILayout.Label("Password (8+ characters)", bodyStyle);
            password = GUILayout.PasswordField(password ?? "", '*', fieldStyle, GUILayout.Height(38));
            if (authTab == 1)
            {
                GUILayout.Label("Workspace invite code", bodyStyle);
                invite = GUILayout.TextField(invite, fieldStyle, GUILayout.Height(38));
            }
            GUILayout.Space(12);
            UnityEngine.GUI.enabled = !busy;
            if (GUILayout.Button(authTab == 0 ? "Sign in" : "Create account and join", activeButtonStyle, GUILayout.Height(42)))
            {
                string url = config.Url, user = username, secret = password;
                if (authTab == 0)
                    Run(() => WorkspaceApi.Login(url, user, secret), reply => Connected(url, user, reply));
                else
                {
                    string name = config.DisplayName, code = invite.Trim();
                    Run(() => WorkspaceApi.Register(url, code, user, name, secret), reply => Connected(url, user, reply));
                }
            }
            UnityEngine.GUI.enabled = true;
            GUILayout.Space(8);
            GUILayout.Label("Use the same account on the web and each PC. Password is not saved here.", mutedStyle);
        }

        private void Connected(string url, string user, JoinReply reply)
        {
            if (string.IsNullOrEmpty(reply.Token)) { status = "Server did not return a game session."; return; }
            config.Url = WorkspaceApi.NormalizeUrl(url);
            config.Token = reply.Token;
            config.Username = user;
            config.AccountID = reply.AccountID;
            config.WorkspaceID = reply.WorkspaceID;
            config.WorkspaceName = reply.WorkspaceName;
            if (!string.IsNullOrEmpty(reply.DisplayName)) config.DisplayName = reply.DisplayName;
            password = "";
            invite = "";
            SaveConfig();
            status = "Connected to " + reply.WorkspaceName;
            RefreshWorkspaces();
        }

        private void DrawLibrary()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label((config.WorkspaceName ?? "Workspace") + (string.IsNullOrEmpty(config.Username) ? "" : "  ·  @" + config.Username), bodyStyle);
            GUILayout.FlexibleSpace();
            UnityEngine.GUI.enabled = !busy;
            if (GUILayout.Button("Sign out", buttonStyle, GUILayout.Width(110), GUILayout.Height(34))) SignOut();
            UnityEngine.GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Space(12);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("My saved blueprints", tab == 0 ? activeButtonStyle : buttonStyle, GUILayout.Height(40))) { tab = 0; scroll = Vector2.zero; }
            if (GUILayout.Button("Workspace library", tab == 1 ? activeButtonStyle : buttonStyle, GUILayout.Height(40))) { tab = 1; scroll = Vector2.zero; }
            if (GUILayout.Button("Workspaces", tab == 2 ? activeButtonStyle : buttonStyle, GUILayout.Height(40))) { tab = 2; workspaceScroll = Vector2.zero; }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            UnityEngine.GUI.enabled = !busy;
            if (GUILayout.Button(tab == 0 ? "Refresh local" : tab == 1 ? "Refresh library" : "Refresh workspaces", buttonStyle, GUILayout.Width(170), GUILayout.Height(34)))
            {
                if (tab == 0) RefreshLocal(); else if (tab == 1) RefreshShared(); else RefreshWorkspaces();
            }
            UnityEngine.GUI.enabled = true;
            GUILayout.EndHorizontal();

            if (tab == 2) DrawWorkspaces();
            else
            {
                scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(260));
                if (tab == 0) DrawLocalList(); else DrawSharedList();
                GUILayout.EndScrollView();
            }
        }

        private void DrawWorkspaces()
        {
            GUILayout.Label("Select the library for publishing and importing:", mutedStyle);
            workspaceScroll = GUILayout.BeginScrollView(workspaceScroll, GUILayout.Height(165));
            foreach (WorkspaceMembership item in workspaces)
            {
                GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(43));
                GUILayout.Label(item.Name + "  ·  " + item.Role, bodyStyle);
                GUILayout.FlexibleSpace();
                UnityEngine.GUI.enabled = !busy && item.ID != config.WorkspaceID;
                if (GUILayout.Button(item.ID == config.WorkspaceID ? "Current" : "Open", buttonStyle,
                    GUILayout.Width(100), GUILayout.Height(32))) SelectWorkspace(item);
                UnityEngine.GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.Space(10);
            GUILayout.Label("Join another workspace with its invite code", mutedStyle);
            invite = GUILayout.TextField(invite ?? "", fieldStyle, GUILayout.Height(38));
            UnityEngine.GUI.enabled = !busy && !string.IsNullOrWhiteSpace(invite);
            if (GUILayout.Button("Join workspace", activeButtonStyle, GUILayout.Height(38))) JoinWorkspace();
            UnityEngine.GUI.enabled = true;
        }

        private void SelectWorkspace(WorkspaceMembership item)
        {
            config.WorkspaceID = item.ID;
            config.WorkspaceName = item.Name;
            shared = Array.Empty<BlueprintSummary>();
            tab = 1;
            SaveConfig();
            RefreshShared();
        }

        private void JoinWorkspace()
        {
            string code = invite.Trim();
            Run(() => WorkspaceApi.JoinWorkspace(config.Url, config.Token, code), joined =>
            {
                invite = "";
                config.WorkspaceID = joined.ID;
                config.WorkspaceName = joined.Name;
                SaveConfig();
                status = "Joined " + joined.Name + ".";
                RefreshWorkspaces();
            });
        }

        private void RefreshWorkspaces()
        {
            Run(() => WorkspaceApi.Workspaces(config.Url, config.Token), reply =>
            {
                workspaces = reply.Workspaces ?? Array.Empty<WorkspaceMembership>();
                WorkspaceMembership selected = Array.Find(workspaces, item => item.ID == config.WorkspaceID);
                if (selected == null && workspaces.Length > 0) selected = workspaces[0];
                if (selected == null)
                {
                    config.WorkspaceID = "";
                    config.WorkspaceName = "";
                    shared = Array.Empty<BlueprintSummary>();
                    status = "No active workspace. Join one on the website.";
                    return;
                }
                config.WorkspaceID = selected.ID;
                config.WorkspaceName = selected.Name;
                SaveConfig();
                RefreshShared();
            });
        }

        private void DrawLocalList()
        {
            if (local.Count == 0) GUILayout.Label("No saved blueprints found. Save one in game first.", mutedStyle);
            foreach (LocalBlueprint item in local)
            {
                GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(43));
                GUILayout.Label(item.Name, bodyStyle);
                GUILayout.FlexibleSpace();
                UnityEngine.GUI.enabled = !busy;
                if (GUILayout.Button("Publish", buttonStyle, GUILayout.Width(110), GUILayout.Height(32))) Publish(item);
                UnityEngine.GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void DrawSharedList()
        {
            if (shared.Length == 0) GUILayout.Label("The workspace has no blueprints yet.", mutedStyle);
            foreach (BlueprintSummary item in shared)
            {
                GUILayout.BeginHorizontal(rowStyle, GUILayout.Height(43));
                GUILayout.Label(item.Name + "  ·  " + item.Author, bodyStyle);
                GUILayout.FlexibleSpace();
                UnityEngine.GUI.enabled = !busy;
                if (GUILayout.Button("Import copy", buttonStyle, GUILayout.Width(110), GUILayout.Height(32))) Import(item);
                UnityEngine.GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private void RefreshLocal()
        {
            try
            {
                local = BlueprintFiles.List();
                status = "Found " + local.Count + " local blueprint(s).";
                Debug.Log("[BlueprintWorkspace] Blueprint directory: " + BlueprintFiles.Root + "; count=" + local.Count);
            }
            catch (Exception e) { status = "Local blueprints: " + e.Message; }
        }

        private void RefreshShared()
        {
            Run(() => WorkspaceApi.List(config.Url, config.Token, config.WorkspaceID), reply =>
            {
                shared = reply.Blueprints ?? Array.Empty<BlueprintSummary>();
                config.WorkspaceName = reply.WorkspaceName;
                status = "Found " + shared.Length + " shared blueprint(s).";
                RefreshProfile();
            });
        }

        private void RefreshProfile()
        {
            Run(() => WorkspaceApi.Me(config.Url, config.Token, config.WorkspaceID), reply =>
            {
                config.Username = reply.Username;
                config.DisplayName = reply.DisplayName;
                config.AccountID = reply.AccountID;
                config.WorkspaceID = reply.WorkspaceID;
                config.WorkspaceName = reply.WorkspaceName;
                SaveConfig();
                status = "Connected as " + reply.DisplayName + ".";
            });
        }

        private void SignOut()
        {
            Run(() => WorkspaceApi.Logout(config.Url, config.Token), _ =>
            {
                config.Token = "";
                config.WorkspaceName = "";
                config.AccountID = "";
                config.WorkspaceID = "";
                shared = Array.Empty<BlueprintSummary>();
                workspaces = Array.Empty<WorkspaceMembership>();
                SaveConfig();
                status = "Signed out on this PC.";
            });
        }

        private void Publish(LocalBlueprint item)
        {
            try
            {
                PublishRequest data = BlueprintFiles.Read(item);
                Run(() => WorkspaceApi.Publish(config.Url, config.Token, config.WorkspaceID, data), reply =>
                {
                    status = "Published " + reply.Name + ".";
                    RefreshShared();
                });
            }
            catch (Exception e) { status = "Publish failed: " + e.Message; }
        }

        private void Import(BlueprintSummary item)
        {
            Run(() => WorkspaceApi.Fetch(config.Url, config.Token, config.WorkspaceID, item.ID), reply =>
            {
                try
                {
                    string name = BlueprintFiles.Import(reply, modFolder);
                    RefreshLocal();
                    status = "Imported " + name + ". Reopen the game's load-blueprint list if needed.";
                }
                catch (Exception e) { status = "Import failed: " + e.Message; }
            });
        }

        private void Run<T>(Func<Task<T>> work, Action<T> success)
        {
            if (busy) return;
            busy = true;
            status = "Working...";
            Task.Run(async () =>
            {
                try
                {
                    T result = await work().ConfigureAwait(false);
                    pending.Enqueue(() => { busy = false; success(result); });
                }
                catch (Exception e)
                {
                    pending.Enqueue(() => { busy = false; status = e.Message; });
                }
            });
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(modFolder);
                File.WriteAllText(Path.Combine(modFolder, "WorkspaceConfig.json"), JsonConvert.SerializeObject(config));
            }
            catch (Exception e) { status = "Could not save workspace settings: " + e.Message; }
        }

        private void EnsureStyles()
        {
            if (panelStyle != null) return;
            panelTexture = Rounded(new Color(0.14f, 0.23f, 0.36f, 0.98f), 10);
            fieldTexture = Rounded(new Color(0.09f, 0.17f, 0.28f, 1f), 7);
            buttonTexture = Rounded(new Color(0.16f, 0.28f, 0.43f, 1f), 7);
            activeTexture = Rounded(new Color(0.47f, 0.68f, 0.93f, 1f), 7);
            rowTexture = Rounded(new Color(0.19f, 0.30f, 0.45f, 0.8f), 6);
            panelStyle = new GUIStyle(UnityEngine.GUI.skin.box) { border = new RectOffset(10, 10, 10, 10), normal = { background = panelTexture }, padding = new RectOffset(0, 0, 0, 0) };
            titleStyle = new GUIStyle(UnityEngine.GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            bodyStyle = new GUIStyle(UnityEngine.GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            mutedStyle = new GUIStyle(bodyStyle) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.72f, 0.80f, 0.91f) } };
            fieldStyle = new GUIStyle(UnityEngine.GUI.skin.textField) { border = new RectOffset(7, 7, 7, 7), fontSize = 16, padding = new RectOffset(12, 12, 8, 8), normal = { background = fieldTexture, textColor = Color.white }, focused = { background = fieldTexture, textColor = Color.white } };
            buttonStyle = new GUIStyle(UnityEngine.GUI.skin.button) { border = new RectOffset(7, 7, 7, 7), fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { background = buttonTexture, textColor = Color.white }, hover = { background = rowTexture, textColor = Color.white } };
            activeButtonStyle = new GUIStyle(buttonStyle) { normal = { background = activeTexture, textColor = new Color(0.05f, 0.13f, 0.23f) } };
            rowStyle = new GUIStyle(UnityEngine.GUI.skin.box) { border = new RectOffset(6, 6, 6, 6), normal = { background = rowTexture }, padding = new RectOffset(10, 8, 4, 4), margin = new RectOffset(0, 0, 3, 3) };
        }

        private static Texture2D Rounded(Color color, int radius)
        {
            const int size = 32;
            var texture = new Texture2D(size, size);
            texture.filterMode = FilterMode.Bilinear;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x - 0.5f, x + 0.5f - (size - radius), 0f);
                float dy = Mathf.Max(radius - y - 0.5f, y + 0.5f - (size - radius), 0f);
                float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
            }
            texture.Apply();
            return texture;
        }
    }
}
