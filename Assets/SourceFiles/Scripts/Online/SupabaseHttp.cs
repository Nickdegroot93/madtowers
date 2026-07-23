using System.Text;
using UnityEngine.Networking;

/// <summary>
/// Builds UnityWebRequests for the Supabase REST surface (BACKEND.md §7: PostgREST over
/// plain HTTPS, no SDK). Callers own sending/disposing; every request carries the anon
/// apikey plus the user's JWT as bearer when a session exists (RLS keys off the bearer).
/// </summary>
public static class SupabaseHttp
{
    private const int TimeoutSeconds = 8;

    /// <summary>POST /rest/v1/rpc/{fn} with a JSON body (server functions are the only
    /// write path for scores/attempts/runs - BACKEND.md §4.3).</summary>
    public static UnityWebRequest Rpc(string fn, string jsonBody) =>
        Post($"{SupabaseConfig.Url}/rest/v1/rpc/{fn}", jsonBody, bearer: BearerOrAnon());

    /// <summary>POST an /auth/v1/* route. Auth routes take the anon key as bearer except
    /// where a caller passes the user token explicitly (identity endpoints).</summary>
    public static UnityWebRequest AuthPost(string path, string jsonBody, string bearer = null) =>
        Post($"{SupabaseConfig.Url}{path}", jsonBody, bearer ?? SupabaseConfig.AnonKey);

    /// <summary>GET an absolute-path-with-query (PostgREST reads).</summary>
    public static UnityWebRequest Get(string pathWithQuery)
    {
        UnityWebRequest req = UnityWebRequest.Get($"{SupabaseConfig.Url}{pathWithQuery}");
        Decorate(req, BearerOrAnon());
        return req;
    }

    private static string BearerOrAnon() =>
        !string.IsNullOrEmpty(SupabaseSession.AccessToken) ? SupabaseSession.AccessToken : SupabaseConfig.AnonKey;

    private static UnityWebRequest Post(string url, string jsonBody, string bearer)
    {
        UnityWebRequest req = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST);
        req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody ?? "{}"));
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        Decorate(req, bearer);
        return req;
    }

    private static void Decorate(UnityWebRequest req, string bearer)
    {
        req.timeout = TimeoutSeconds;
        req.SetRequestHeader("apikey", SupabaseConfig.AnonKey);
        req.SetRequestHeader("Authorization", $"Bearer {bearer}");
    }

    /// <summary>Escape a string for embedding inside a hand-built JSON body (JsonUtility
    /// serializes whole objects, but RPC bodies are small enough to compose by hand).</summary>
    public static string JsonEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        StringBuilder sb = new StringBuilder(s.Length + 8);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
