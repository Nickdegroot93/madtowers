using System;
using System.Collections;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Scripting;

/// <summary>Native account chooser only. Supabase verifies tokens and owns identity.</summary>
[Preserve]
public sealed class NativeIdentity : MonoBehaviour
{
    [Serializable]
    public sealed class Credential
    {
        public string requestId;
        public string idToken;
        public string error;
        [NonSerialized] public string nonce;
    }

    private static NativeIdentity _instance;
    private Action<Credential> _done;
    private string _requestId;
    private string _nonce;

    public static bool Supports(string provider)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        return provider == "google";
#elif UNITY_IOS && !UNITY_EDITOR
        return provider == "apple";
#else
        return false;
#endif
    }

    public static void SignIn(string provider, Action<Credential> done)
    {
        if (!Supports(provider))
        {
            done(new Credential { error = "Use a mobile build to sign in" });
            return;
        }
        if (_instance == null)
        {
            var host = new GameObject("NativeIdentity");
            DontDestroyOnLoad(host);
            _instance = host.AddComponent<NativeIdentity>();
        }
        _instance.Begin(provider, done);
    }

    private void Begin(string provider, Action<Credential> done)
    {
        if (_done != null) { done(new Credential { error = "Sign-in already in progress" }); return; }
        _done = done;
        _requestId = Guid.NewGuid().ToString("N");
        string requestId = _requestId;
        try
        {
            _nonce = CreateNonce();
            string digest = HashNonce(_nonce);
#if UNITY_ANDROID && !UNITY_EDITOR
            using (var bridge = new AndroidJavaClass("com.nickdegroot.hazardheights.auth.GoogleIdentity"))
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                bridge.CallStatic("signIn", activity, gameObject.name, requestId,
                    SupabaseConfig.GoogleWebClientId, digest);
#elif UNITY_IOS && !UNITY_EDITOR
            HHAppleSignIn(gameObject.name, requestId, digest);
#endif
            StartCoroutine(TimeoutCo(requestId));
        }
        catch (Exception)
        {
            Complete(new Credential { requestId = requestId, error = "Couldn't open sign-in. Try again" });
        }
    }

    internal static string CreateNonce()
    {
        byte[] bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string HashNonce(string nonce)
    {
        using (var sha = SHA256.Create())
            return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(nonce)))
                .Replace("-", "").ToLowerInvariant();
    }

    // UnitySendMessage dispatches on Unity's main thread. Request IDs discard stale callbacks.
    [Preserve]
    public void OnNativeCredential(string json)
    {
        Credential credential;
        try { credential = JsonUtility.FromJson<Credential>(json); }
        catch { return; }
        if (credential == null || credential.requestId != _requestId || _done == null) return;
        if (string.IsNullOrEmpty(credential.idToken) && string.IsNullOrEmpty(credential.error))
            credential.error = "No sign-in credential received";
        Complete(credential);
    }

    private IEnumerator TimeoutCo(string requestId)
    {
        yield return new WaitForSecondsRealtime(180f);
        if (_requestId == requestId && _done != null)
            Complete(new Credential { requestId = requestId, error = "Sign-in timed out. Try again" });
    }

    private void Complete(Credential credential)
    {
        if (_done == null || credential.requestId != _requestId) return;
        var callback = _done;
        credential.nonce = _nonce;
        _done = null;
        _requestId = null;
        _nonce = null;
        callback(credential);
    }

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void HHAppleSignIn(
        string host, string requestId, string nonceHash);
#endif
}
