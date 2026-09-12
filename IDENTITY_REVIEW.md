# Identity code review — 2026-09-12

Scope: the new Google/Apple bridges, Supabase linking/recovery transaction, account
ownership changes, menu integration, build hooks and related save/run/ad/purchase guards.
**All three bug findings fixed on 2026-09-12.** Their original failure descriptions
are retained below; the separate maintenance notes are not claimed as completed.

Validation: **166 assertions pass** through `python3 Tools/IdentityChecks/run.py`,
including a permanent fixture using the real save/cache classes. Editor and Android
C# compilation pass (existing deprecation warnings remain). Nick confirmed Android
device testing works on 2026-09-12; per-scenario/device details were not recorded.
iOS acceptance remains deferred.

## Findings

### 1. [Fixed, P2] Reconcile identity after a partially successful link

Location: `Assets/SourceFiles/Scripts/Online/OnlineService.Identity.cs:152` and
`Assets/SourceFiles/Scripts/Online/OnlineService.cs:461`.

The ID-token exchange can link the guest successfully on Supabase before a subsequent
`mark_linked`, profile, attempts or merge request fails. The client discards the new
auth response and still displays a guest. Later, a normal token refresh learns that
the same user is now permanent, but `StoreAuthResponse` does not update `IsLinked`.
The sign-in button remains visible and `BeginIdentity` refuses it as already linked.
`RetryConnect` while Ready only refreshes attempts/progress, so it does not repair this
identity presentation. Restarting and completing boot does.

Reproduced: successful same-user token exchange → `mark_linked` 503 → ordinary token
refresh reporting `is_anonymous=false` → `IsLinked=false` and retry returns
`This account is already linked`.

Resolution: retain a committed in-place link immediately, reconcile linked status after
refresh, and track pending game initialization. A retry uses the saved session without
opening native sign-in again; reconnect reloads a pending profile. Regression checks
cover both failed game RPCs and a lost original auth response.

Supabase's [linking implementation](https://github.com/supabase/auth/blob/master/internal/api/identity.go)
updates the anonymous user, and its [ID-token endpoint](https://github.com/supabase/auth/blob/master/internal/api/token_oidc.go)
commits that transaction before returning. A later game RPC cannot roll that back.
These sources verify the API behavior; no live account was linked during this review.

### 2. [Fixed, P2] Preserve the guest caches when durable session storage fails

Location: `Assets/SourceFiles/Scripts/Online/OnlineService.Identity.cs:179`.

`BindOnlineAccount(target)` clears the guest's XP and meter caches before `TryStore`.
On session-write failure, binding the guest again clears them again; it does not restore
their previous values. The error therefore leaves the original account selected but
changes its save. The final attempts refresh does not fetch profile XP.

Reproduced with the production `ProgressStore` and `SupabaseSession`: guest XP 123 →
recover another account → fail the session temporary-file write → sign-in fails,
guest remains selected, XP becomes 0 in memory and the progress file. Server XP is not
deleted and a subsequent successful profile load can recover it.

Resolution: persist the destination session before touching the guest caches. Boot
binds the durable session's owner before network refresh, so mismatched caches cannot
survive an offline refresh failure. Checks cover the exact save file after failure,
retry after storage recovers, and an interrupted transition followed by offline boot.

### 3. [Fixed, P2] Reset ad-refill state when the account changes

Location: `Assets/SourceFiles/Scripts/Online/OnlineService.Identity.cs:188`.

The account-switch reset covers `AttemptsSync` and `ProgressSync`, but omits
`AttemptsService._adRefillDenied`. A guest whose refill was refused can recover an
eligible account and still have its Watch Ad option disabled. Applying the new budget
does not clear this latch; only the process/subsystem reset does.

Reproduced with the production attempts classes: guest receives a `rate_limited`
refill response → successful recovery → destination budget is 10, but
`AttemptsService.AdRefillAvailable` remains false despite an available ad provider.

Resolution: the shared account reset now clears AttemptsService's denial and budget.
Recovery, deletion and replacement of a rejected session use it. Outstanding refill
polls check their originating user before requesting data or reporting a reward.

## Code quality and maintainability

- **DRY:** native Android dependency versions are handwritten in `mainTemplate.gradle`
  and declared in `NativeIdentityDependencies.xml`; EDM4U also emits them in the same
  Gradle file. The current versions match, so this is maintenance duplication rather
  than a demonstrated build failure. Keep one authored version declaration.
- **DRY:** `AttemptsSync.Refresh` duplicates `ApplySnapshot` parsing/application, and
  boot versus identity recovery repeat profile application. Share these narrow operations
  and centralize account-state resets to prevent omissions such as finding 3.
- **Diagnostics:** `IdentityRpcCo` drops every failed RPC without a stage/status log;
  the guarded transaction also discards exception details. Record operation, status,
  safe error code and exception type without tokens or raw provider response bodies.
  This will make closed-test failures diagnosable.
- **Comments:** `AttemptsSync` says it never writes anything even though applying a
  premium verdict can write the save. It also has two adjacent XML summaries above
  `RefreshInFlight`. The ProgressStore cloud-seam comment says only ProgressSync calls
  it, which is no longer true. Update these while touching the relevant methods.
- **Runtime behavior:** provider availability is platform-gated, the client ID is
  centralized, the HTTP builder owns the URL/key configuration, and nonce generation
  uses cryptographic randomness. I found no need for a general-purpose authentication
  framework or additional runtime configuration to support the current two providers.

## Original review evidence and remaining limits

- Existing `python3 Tools/IdentityChecks/run.py`: **138 assertions pass**.
- A supplemental isolated probe reproduced all three findings using production
  OnlineService/Identity, SupabaseHttp/Session, ProgressStore, AttemptsSync,
  AttemptsService and XpSystem. Network/native UI and remaining game services were
  doubles; files were confined to temporary directories. The temporary reproduction
  script was `/tmp/hh_identity_review.py`. It has been superseded by the permanent
  `Tools/IdentityChecks/RecoveryChecks.cs.txt` regression fixture.
- The existing suite substitutes the save and attempts services, runs coroutines
  synchronously, and uses Newtonsoft in place of Unity JsonUtility. It does not prove
  native timeout/stale-callback handling, Unity coroutine scheduling, actual JSON
  default-field behavior, account-scoped run-file migration or full recovery rollback.
  In particular, its storage-failure test calls `TryStore` directly and misses finding 2.
- Checked nonce hashing, ID-token linking and Google credential type handling against
  [Supabase's endpoint source](https://github.com/supabase/auth/blob/master/internal/api/token_oidc.go)
  and [Android's implementation guide](https://developer.android.com/identity/sign-in/credential-manager-siwg-implementation).
- No physical-device login, Play-distributed build or iOS native build was exercised.
  iOS build acceptance and Apple account-deletion grant revocation remain deferred in
  [IOS_TESTING.md](IOS_TESTING.md), rather than being newly discovered Android blockers.
