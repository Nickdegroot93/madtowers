using System;
using System.IO;
using UnityEngine;

/// <summary>
/// The persisted auth session (BACKEND.md §3.2): tokens + who we are. Its own small file,
/// NOT part of ProgressStore - identity is not progress, and clearing one must never touch
/// the other. The refresh token is this device's only proof of ownership of an anonymous
/// account (uninstall = account gone until the player links, BACKEND.md §3.2).
/// </summary>
public static class SupabaseSession
{
    [Serializable]
    private class SessionData
    {
        public string accessToken;
        public string refreshToken;
        public string userId;
        public long expiresAtUnixUtc;
        public bool isAnonymous = true;
    }

    private static SessionData _data;
    private static bool _loaded;

    private static string FilePath => Path.Combine(Application.persistentDataPath, "online_session.json");

    public static string AccessToken { get { EnsureLoaded(); return _data?.accessToken; } }
    public static string RefreshToken { get { EnsureLoaded(); return _data?.refreshToken; } }
    public static string UserId { get { EnsureLoaded(); return _data?.userId; } }
    public static bool IsAnonymous { get { EnsureLoaded(); return _data?.isAnonymous ?? true; } }
    public static long ExpiresAtUnixUtc { get { EnsureLoaded(); return _data?.expiresAtUnixUtc ?? 0; } }

    public static bool HasSession => !string.IsNullOrEmpty(RefreshToken);

    /// <summary>Access token expires within the next minute (or already has) - refresh
    /// before using it.</summary>
    public static bool NeedsRefresh =>
        ExpiresAtUnixUtc - DateTimeOffset.UtcNow.ToUnixTimeSeconds() < 60;

    public static void Store(string accessToken, string refreshToken, string userId,
                             long expiresAtUnixUtc, bool isAnonymous)
    {
        _data = new SessionData
        {
            accessToken = accessToken,
            refreshToken = refreshToken,
            userId = userId,
            expiresAtUnixUtc = expiresAtUnixUtc,
            isAnonymous = isAnonymous,
        };
        _loaded = true;
        try
        {
            // Atomic: this file is the only proof of ownership of an anonymous account and
            // it rewrites on every token rotation - a kill mid-write must not truncate it.
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonUtility.ToJson(_data));
            if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
            else File.Move(tmp, FilePath);
        }
        catch (Exception e)
        {
            Debug.LogError($"[Online] Session save failed: {e.Message}");
        }
    }

    /// <summary>Forget the session (definitively-invalid refresh token or account deletion).
    /// The next boot signs up a fresh anonymous account.</summary>
    public static void Clear()
    {
        _data = null;
        _loaded = true;
        try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch { /* best effort */ }
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(FilePath))
                _data = JsonUtility.FromJson<SessionData>(File.ReadAllText(FilePath));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Online] Could not read session, starting signed out: {e.Message}");
            _data = null;
        }
    }
}
