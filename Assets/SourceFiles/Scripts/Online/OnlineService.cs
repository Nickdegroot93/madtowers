using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// The online facade (BACKEND.md §2/§3): silent anonymous sign-in on boot, session
/// restore/refresh, profile (display name), and the shared RPC path every other online
/// service rides. A hidden DontDestroyOnLoad host owns all coroutines/UnityWebRequests
/// (MusicPlayer pattern) so the single-scene reload churn never kills a request in flight.
///
/// The game never blocks on this: services observe State/StateChanged and degrade. Campaign
/// run starts are the one hard gate, and that lives in RunGate (BACKEND.md §5.1).
/// </summary>
public class OnlineService : MonoBehaviour
{
    public enum OnlineState { Disabled, Connecting, Ready, Offline }

    /// <summary>Mirrors SupabaseConfig.Enabled: false = the whole online layer is inert.</summary>
    public static bool Enabled => SupabaseConfig.Enabled;

    public static OnlineState State { get; private set; } = OnlineState.Disabled;

    /// <summary>Authed, profile loaded, server reachable as of the last exchange.</summary>
    public static bool IsReady => State == OnlineState.Ready;

    /// <summary>Server display name ("Builder-1234" until claimed). Falls back to the old
    /// placeholder while the profile hasn't loaded so UI never renders an empty name.</summary>
    public static string DisplayName => string.IsNullOrEmpty(_displayName) ? "PLAYER ONE" : _displayName;

    public static bool IsLinked { get; private set; }

    /// <summary>Fired on the main thread whenever State, DisplayName or IsLinked changes.</summary>
    public static event Action StateChanged;

    private static OnlineService _instance;
    private static string _displayName;
    private static bool _booting;
    private static int _failedBoots;

    // Auto-retry ladder after a failed boot; after the last rung we stay Offline until
    // RetryConnect() or an app-focus regain (menu is timeScale=0 -> realtime waits only).
    private static readonly float[] RetryDelays = { 5f, 15f, 60f };

    private enum RefreshOutcome { Ok, Rejected, NetworkFail }

    [Serializable]
    private class AuthUser
    {
        public string id;
        public bool is_anonymous;
    }

    [Serializable]
    private class AuthResponse
    {
        public string access_token;
        public string refresh_token;
        public long expires_at;
        public int expires_in;
        public AuthUser user;
    }

    [Serializable]
    private class ProfileDto
    {
        public string display_name;
        public bool is_linked;
    }

    [Serializable]
    private class ClaimNameDto
    {
        public bool ok;
        public string reason;
        public string display_name;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        _instance = null;
        _displayName = null;
        _booting = false;
        _failedBoots = 0;
        _transportFailStreak = 0;
        _refreshInFlight = false;
        State = OnlineState.Disabled;
        IsLinked = false;
        StateChanged = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!Enabled) return;
        EnsureInstance();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        GameObject host = new GameObject("OnlineService");
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<OnlineService>();
    }

    private void Awake()
    {
        Application.focusChanged += HandleFocusChanged;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded += HandleSceneLoaded;
        ProgressSync.Init();
        StartCoroutine(BootCo());
    }

    private void OnDestroy()
    {
        Application.focusChanged -= HandleFocusChanged;
        UnityEngine.SceneManagement.SceneManager.sceneLoaded -= HandleSceneLoaded;
    }

    private void HandleSceneLoaded(UnityEngine.SceneManagement.Scene scene,
                                   UnityEngine.SceneManagement.LoadSceneMode mode)
    {
        // Menu returns are scene reloads; without this, a stale meter count and stuck
        // finish reports would wait for an app refocus that may never come in-editor.
        if (!Enabled || !IsReady) return;
        AttemptsSync.Refresh();
        RunGate.RetryPendingFinishes();
    }

    /// <summary>Manual retry after Offline (the UI's RETRY buttons land here).</summary>
    public static void RetryConnect()
    {
        if (!Enabled || _instance == null || _booting) return;
        _failedBoots = 0;
        _instance.StartCoroutine(_instance.BootCo());
    }

    /// <summary>Run a coroutine on the persistent host (for services without their own).
    /// Creates the host on demand: callers hooked to AfterSceneLoad can run before
    /// Bootstrap does, and silently dropping their coroutine loses one-shot UI (the
    /// link prompt missed the first menu of every session that way).</summary>
    public static Coroutine Run(IEnumerator co)
    {
        if (!Enabled) return null;
        EnsureInstance();
        return _instance.StartCoroutine(co);
    }

    /// <summary>POST /rest/v1/rpc/{fn} and JsonUtility-parse the object reply. Handles token
    /// refresh + one retry on 401. onErr receives the response body or transport error.</summary>
    public static void RpcObject<T>(string fn, string jsonBody, Action<T> onOk, Action<string> onErr)
    {
        if (!Enabled || _instance == null)
        {
            onErr?.Invoke("online disabled");
            return;
        }
        _instance.StartCoroutine(_instance.SendWithAuthCo(
            () => SupabaseHttp.Rpc(fn, jsonBody),
            body =>
            {
                T parsed;
                try { parsed = JsonUtility.FromJson<T>(body); }
                catch (Exception e)
                {
                    onErr?.Invoke($"bad reply: {e.Message}");
                    return;
                }
                if (parsed == null) { onErr?.Invoke("empty reply"); return; }
                onOk?.Invoke(parsed);
            },
            onErr));
    }

    /// <summary>Like RpcObject but hands back the raw JSON body (for replies applied as-is,
    /// e.g. the merged progress payload).</summary>
    public static void RpcRaw(string fn, string jsonBody, Action<string> onOk, Action<string> onErr)
    {
        if (!Enabled || _instance == null)
        {
            onErr?.Invoke("online disabled");
            return;
        }
        _instance.StartCoroutine(_instance.SendWithAuthCo(
            () => SupabaseHttp.Rpc(fn, jsonBody), onOk, onErr));
    }

    /// <summary>Claim a custom display name (BACKEND.md §3.5). done(ok, reason) - reason is
    /// the server's "taken|invalid|profane" verdict when ok is false.</summary>
    public static void ClaimDisplayName(string name, Action<bool, string> done)
    {
        RpcObject<ClaimNameDto>("claim_display_name",
            $"{{\"p_name\":\"{SupabaseHttp.JsonEscape(name)}\"}}",
            dto =>
            {
                if (dto.ok && !string.IsNullOrEmpty(dto.display_name))
                {
                    _displayName = dto.display_name;
                    StateChanged?.Invoke();
                }
                done?.Invoke(dto.ok, dto.reason);
            },
            err => done?.Invoke(false, "offline"));
    }

    // Link scaffolds (BACKEND.md §3.3): the real flows need the native plugins - Sign in with
    // Apple / Google sign-in hand us an OS-verified identity token, which upgrades the
    // anonymous Supabase user IN PLACE (same user_id, progress kept). These are the call
    // sites; in the editor and until the plugins ship they fail immediately with honest copy.
    public static void LinkWithApple(Action<bool, string> done) =>
        done?.Invoke(false, "Sign-in arrives with the mobile build");

    public static void LinkWithGoogle(Action<bool, string> done) =>
        done?.Invoke(false, "Sign-in arrives with the mobile build");

    // ---- boot ---------------------------------------------------------------------------

    private IEnumerator BootCo()
    {
        if (_booting) yield break;
        _booting = true;
        SetState(OnlineState.Connecting);

        // Session restore. A network failure here must NOT fall through to a fresh signup -
        // that would orphan the existing account (an anonymous user's refresh token is the
        // only key to it). Only a definitive rejection clears the session.
        if (SupabaseSession.HasSession)
        {
            RefreshOutcome outcome = RefreshOutcome.NetworkFail;
            yield return RefreshCo(o => outcome = o);
            if (outcome == RefreshOutcome.NetworkFail)
            {
                FailBoot();
                yield break;
            }
            if (outcome == RefreshOutcome.Rejected)
            {
                Debug.LogWarning("[Online] Stored session rejected; starting a fresh account.");
                SupabaseSession.Clear();
            }
        }

        if (!SupabaseSession.HasSession)
        {
            bool signedUp = false;
            yield return AnonymousSignUpCo(ok => signedUp = ok);
            if (!signedUp)
            {
                FailBoot();
                yield break;
            }
        }

        // Profile (auto-created by the server on signup - "Builder-XXXX", BACKEND.md §3.2).
        bool profileOk = false;
        yield return SendWithAuthCo(
            () => SupabaseHttp.Rpc("get_profile", "{}"),
            body =>
            {
                ProfileDto dto = null;
                try { dto = JsonUtility.FromJson<ProfileDto>(body); } catch { /* falls through */ }
                if (dto == null || string.IsNullOrEmpty(dto.display_name)) return;
                _displayName = dto.display_name;
                IsLinked = dto.is_linked;
                profileOk = true;
            },
            null);
        if (!profileOk)
        {
            FailBoot();
            yield break;
        }

        _failedBoots = 0;
        _booting = false;
        SetState(OnlineState.Ready);

        AttemptsSync.Refresh();
        RunGate.RetryPendingFinishes();
        ProgressSync.OnReady();
    }

    private void FailBoot()
    {
        _booting = false;
        SetState(OnlineState.Offline);
        if (_failedBoots < RetryDelays.Length)
            StartCoroutine(RetryLaterCo(RetryDelays[_failedBoots++]));
    }

    private IEnumerator RetryLaterCo(float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(delaySeconds);
        if (State == OnlineState.Offline && !_booting) StartCoroutine(BootCo());
    }

    private void HandleFocusChanged(bool hasFocus)
    {
        if (!Enabled) return;
        if (!hasFocus)
        {
            ProgressSync.OnBackground();
            return;
        }
        if (State == OnlineState.Offline) RetryConnect();
        else if (IsReady)
        {
            AttemptsSync.Refresh();
            RunGate.RetryPendingFinishes();
            ProgressSync.OnFocusRegained();
        }
    }

    private static void SetState(OnlineState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke();
    }

    // ---- auth exchanges -----------------------------------------------------------------

    private IEnumerator AnonymousSignUpCo(Action<bool> done)
    {
        // GoTrue anonymous sign-in: POST /auth/v1/signup with an empty body creates a real
        // user (role authenticated, is_anonymous claim). BACKEND.md §3.2.
        using (UnityWebRequest req = SupabaseHttp.AuthPost("/auth/v1/signup", "{}"))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[Online] Anonymous sign-in failed: {Describe(req)}");
                done(false);
                yield break;
            }
            done(StoreAuthResponse(req.downloadHandler.text));
        }
    }

    // Single-flight: refresh tokens ROTATE server-side, so concurrent racers (focus regain
    // fires the attempts refresh, queued finishes and a merge in one frame) must consume one
    // exchange's outcome rather than each spending the same token.
    private static bool _refreshInFlight;
    private static RefreshOutcome _refreshOutcome;

    private IEnumerator RefreshCo(Action<RefreshOutcome> done)
    {
        if (_refreshInFlight)
        {
            while (_refreshInFlight) yield return null;
            done?.Invoke(_refreshOutcome);
            yield break;
        }
        _refreshInFlight = true;
        yield return RefreshExchangeCo(o => _refreshOutcome = o);
        _refreshInFlight = false;
        done?.Invoke(_refreshOutcome);
    }

    private IEnumerator RefreshExchangeCo(Action<RefreshOutcome> done)
    {
        string body = $"{{\"refresh_token\":\"{SupabaseHttp.JsonEscape(SupabaseSession.RefreshToken)}\"}}";
        using (UnityWebRequest req = SupabaseHttp.AuthPost("/auth/v1/token?grant_type=refresh_token", body))
        {
            yield return req.SendWebRequest();
            if (req.result == UnityWebRequest.Result.Success)
            {
                done?.Invoke(StoreAuthResponse(req.downloadHandler.text)
                    ? RefreshOutcome.Ok : RefreshOutcome.Rejected);
                yield break;
            }
            // Rejected = this token is definitively dead, per status or a GoTrue auth error
            // marker. Anything else (rate limit, captive portal, 5xx, odd 4xx) is a network
            // failure: the session is the only key to an anonymous account, never discard it
            // on an ambiguous answer.
            string replyBody = req.downloadHandler?.text ?? "";
            bool rejected = req.responseCode == 401 || req.responseCode == 403 || req.responseCode == 404
                || (req.responseCode == 400 &&
                    (replyBody.Contains("invalid_grant") || replyBody.Contains("refresh_token_not_found")
                     || replyBody.Contains("already_used") || replyBody.Contains("refresh_token_revoked")));
            if (rejected) Debug.LogWarning($"[Online] Session refresh rejected: {Describe(req)}");
            done?.Invoke(rejected ? RefreshOutcome.Rejected : RefreshOutcome.NetworkFail);
        }
    }

    private static bool StoreAuthResponse(string json)
    {
        AuthResponse auth = null;
        try { auth = JsonUtility.FromJson<AuthResponse>(json); } catch { /* falls through */ }
        if (auth == null || string.IsNullOrEmpty(auth.access_token) || auth.user == null)
        {
            Debug.LogWarning("[Online] Unreadable auth reply.");
            return false;
        }
        long expiresAt = auth.expires_at > 0
            ? auth.expires_at
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds() + Math.Max(60, auth.expires_in);
        SupabaseSession.Store(auth.access_token, auth.refresh_token, auth.user.id,
            expiresAt, auth.user.is_anonymous);
        return true;
    }

    // ---- transport ------------------------------------------------------------------------

    /// <summary>Send with a fresh-token guarantee: proactive refresh when near expiry, plus
    /// one refresh-and-retry on a 401 (token revoked server-side).</summary>
    private IEnumerator SendWithAuthCo(Func<UnityWebRequest> build, Action<string> onOk, Action<string> onErr)
    {
        if (SupabaseSession.HasSession && SupabaseSession.NeedsRefresh)
            yield return RefreshCo(null); // best effort; a hard failure hits the 401 path below

        for (int attempt = 0; attempt < 2; attempt++)
        {
            string errText;
            using (UnityWebRequest req = build())
            {
                yield return req.SendWebRequest();
                NoteTransport(req.result != UnityWebRequest.Result.ConnectionError);
                if (req.result == UnityWebRequest.Result.Success)
                {
                    onOk?.Invoke(req.downloadHandler.text);
                    yield break;
                }
                if (req.responseCode == 401 && attempt == 0 && SupabaseSession.HasSession)
                {
                    RefreshOutcome outcome = RefreshOutcome.NetworkFail;
                    yield return RefreshCo(o => outcome = o);
                    if (outcome == RefreshOutcome.Ok) continue;
                    // Definitive mid-session rejection: KEEP the session (a fresh signup
                    // would orphan the account's server data) but stop claiming Ready -
                    // RetryConnect / the backoff ladder keeps re-trying the refresh.
                    if (outcome == RefreshOutcome.Rejected && IsReady)
                        SetState(OnlineState.Offline);
                }
                errText = Describe(req);
            }
            onErr?.Invoke(errText);
            yield break;
        }
    }

    // Mid-session reachability: Ready is a claim the UI trusts (OFFLINE chips, gate copy),
    // so losing the network after boot must flip it. Two consecutive connection-level
    // failures = offline (one can be a blip); any reachable reply recovers. Protocol errors
    // (4xx/5xx) count as reachable - the server answered.
    private static int _transportFailStreak;

    private static void NoteTransport(bool reachable)
    {
        if (reachable)
        {
            _transportFailStreak = 0;
            if (State == OnlineState.Offline && !string.IsNullOrEmpty(_displayName))
                SetState(OnlineState.Ready);
            return;
        }
        _transportFailStreak++;
        if (_transportFailStreak >= 2 && State == OnlineState.Ready)
        {
            SetState(OnlineState.Offline);
            if (_instance != null) _instance.StartCoroutine(_instance.RetryLaterCo(15f));
        }
    }

    private static string Describe(UnityWebRequest req)
    {
        string body = req.downloadHandler?.text;
        return string.IsNullOrEmpty(body) ? $"{req.error} ({req.responseCode})" : body;
    }
}
