using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace BlueprintWorkspace
{
    internal sealed class WorkspaceConfig
    {
        public string Url = "";
        public string Token = "";
        public string WorkspaceName = "";
        public string DisplayName = "";
        public string Username = "";
        public string AccountID = "";
        public string WorkspaceID = "";
    }

    internal sealed class JoinReply
    {
        [JsonProperty("token")] public string Token { get; set; }
        [JsonProperty("workspace_name")] public string WorkspaceName { get; set; }
        [JsonProperty("workspace_id")] public string WorkspaceID { get; set; }
        [JsonProperty("account_id")] public string AccountID { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
    }

    internal sealed class ProfileReply
    {
        [JsonProperty("username")] public string Username { get; set; }
        [JsonProperty("display_name")] public string DisplayName { get; set; }
        [JsonProperty("workspace_name")] public string WorkspaceName { get; set; }
        [JsonProperty("workspace_id")] public string WorkspaceID { get; set; }
        [JsonProperty("account_id")] public string AccountID { get; set; }
    }

    internal sealed class BlueprintSummary
    {
        [JsonProperty("id")] public string ID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("author")] public string Author { get; set; }
        [JsonProperty("version")] public string Version { get; set; }
    }

    internal sealed class ListReply
    {
        [JsonProperty("workspace_name")] public string WorkspaceName { get; set; }
        [JsonProperty("blueprints")] public BlueprintSummary[] Blueprints { get; set; }
    }

    internal sealed class WorkspaceMembership
    {
        [JsonProperty("id")] public string ID { get; set; }
        [JsonProperty("name")] public string Name { get; set; }
        [JsonProperty("role")] public string Role { get; set; }
    }

    internal sealed class WorkspacesReply
    {
        [JsonProperty("workspaces")] public WorkspaceMembership[] Workspaces { get; set; }
        [JsonProperty("current_workspace_id")] public string CurrentWorkspaceID { get; set; }
    }

    internal class PublishRequest
    {
        [JsonProperty("name")] public string Name;
        [JsonProperty("blueprint")] public string Blueprint;
        [JsonProperty("version")] public string Version;
    }

    internal sealed class FetchReply : PublishRequest
    {
        [JsonProperty("id")] public string ID { get; set; }
    }

    internal static class WorkspaceApi
    {
        private static readonly HttpClient Client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };

        internal static string NormalizeUrl(string text)
        {
            if (!Uri.TryCreate((text ?? "").Trim().TrimEnd('/'), UriKind.Absolute, out Uri uri))
                throw new InvalidOperationException("Enter a valid server URL.");
            if (uri.Scheme != Uri.UriSchemeHttps &&
                !(uri.Scheme == Uri.UriSchemeHttp && (uri.Host == "localhost" || uri.Host == "127.0.0.1")))
                throw new InvalidOperationException("Use HTTPS for a VPS server (HTTP is allowed only on localhost).");
            if (uri.UserInfo != "" || uri.Query != "" || uri.Fragment != "")
                throw new InvalidOperationException("Server URL must not contain credentials, query, or fragment.");
            return uri.AbsoluteUri.TrimEnd('/');
        }

        internal static Task<JoinReply> Register(string url, string invite, string username, string displayName, string password) =>
            Request<JoinReply>(url, "api/register", "POST", "", new { invite, username, display_name = displayName, password, client = "game" });

        internal static Task<JoinReply> Login(string url, string username, string password) =>
            Request<JoinReply>(url, "api/login", "POST", "", new { username, password, client = "game" });

        internal static Task<ProfileReply> Me(string url, string token, string workspaceID) =>
            Request<ProfileReply>(url, "api/me", "GET", token, null, workspaceID);

        internal static Task<WorkspacesReply> Workspaces(string url, string token) =>
            Request<WorkspacesReply>(url, "api/workspaces", "GET", token, null);

        internal static Task<WorkspaceMembership> JoinWorkspace(string url, string token, string invite) =>
            Request<WorkspaceMembership>(url, "api/workspaces/join", "POST", token, new { invite });

        internal static Task<object> Logout(string url, string token) =>
            Request<object>(url, "api/logout", "POST", token, new { });

        internal static Task<ListReply> List(string url, string token, string workspaceID) =>
            Request<ListReply>(url, "api/blueprints", "GET", token, null, workspaceID);

        internal static Task<BlueprintSummary> Publish(string url, string token, string workspaceID, PublishRequest data) =>
            Request<BlueprintSummary>(url, "api/blueprints", "POST", token, data, workspaceID);

        internal static Task<FetchReply> Fetch(string url, string token, string workspaceID, string id) =>
            Request<FetchReply>(url, "api/blueprints/" + Uri.EscapeDataString(id), "GET", token, null, workspaceID);

        private static async Task<T> Request<T>(string url, string path, string method, string token, object data, string workspaceID = "")
        {
            string baseUrl = NormalizeUrl(url);
            using (var request = new HttpRequestMessage(new HttpMethod(method), baseUrl + "/" + path))
            {
                if (!string.IsNullOrEmpty(token)) request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                if (!string.IsNullOrEmpty(workspaceID)) request.Headers.TryAddWithoutValidation("X-Workspace-ID", workspaceID);
                if (data != null) request.Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");
                using (var response = await Client.SendAsync(request).ConfigureAwait(false))
                {
                    string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("Server returned " + (int)response.StatusCode + ": " + SafeError(body));
                    return JsonConvert.DeserializeObject<T>(body);
                }
            }
        }

        private static string SafeError(string body)
        {
            try { return (string)JsonConvert.DeserializeObject<Newtonsoft.Json.Linq.JObject>(body)["error"] ?? "request failed"; }
            catch { return "request failed"; }
        }
    }
}
