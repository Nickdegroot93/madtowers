// AdMob rewarded server-side verification (SSV).
//
// Google calls this URL when a player finishes a rewarded ad. Everything in the
// query string BEFORE "&signature=" is signed with one of Google's rotating ECDSA
// P-256 keys; we verify it and only then grant the attempts. The device is not part
// of the claim any more, which is the whole point: before this, the app simply told
// our server "I watched an ad" and nothing could contradict it.
//
// Registered in the AdMob console per ad unit. custom_data carries the Supabase
// user id, set by the client via SetServerSideVerificationOptions.
//
// Docs: developers.google.com/admob/unity/ssv
import { createClient } from "https://esm.sh/@supabase/supabase-js@2.45.0";

const KEY_URL = "https://www.gstatic.com/admob/reward/verifier-keys.json";

// Our own rewarded units (the numeric half of ca-app-pub-4384624714813425/…), plus
// Google's public test units so a development build still exercises the real path.
// A signature proves the callback came from Google - NOT that it came from our app.
const ALLOWED_AD_UNITS = new Set([
  "2353049753",   // Hazard Heights Android — attempts_refill
  "9768505345",   // Hazard Heights iOS     — attempts_refill
  "5224354917",   // Google sample rewarded, Android
  "1712485313",   // Google sample rewarded, iOS
]);

const UUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

type VerifierKey = { keyId: number; pem: string; base64: string };

// Google rotates these; cache but never longer than 24h (their instruction).
let keyCache: { at: number; keys: Map<string, CryptoKey> } | null = null;
const KEY_TTL_MS = 6 * 60 * 60 * 1000;

function b64ToBytes(b64: string): Uint8Array {
  const bin = atob(b64.replace(/-/g, "+").replace(/_/g, "/"));
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

/**
 * Google signs with DER-encoded ECDSA; WebCrypto verifies raw r||s. Converting is
 * not optional - handing DER straight to verify() returns false for every callback,
 * which fails CLOSED (nobody gets paid) rather than open, but is still broken.
 */
function derToRaw(der: Uint8Array): Uint8Array | null {
  if (der[0] !== 0x30) return null;
  let i = 2;
  if (der[1] & 0x80) i = 2 + (der[1] & 0x7f);   // long-form length
  if (der[i] !== 0x02) return null;
  const rLen = der[i + 1];
  let r = der.slice(i + 2, i + 2 + rLen);
  i = i + 2 + rLen;
  if (der[i] !== 0x02) return null;
  const sLen = der[i + 1];
  let s = der.slice(i + 2, i + 2 + sLen);

  // Strip the sign byte DER adds, then left-pad each to 32 bytes.
  const fix = (v: Uint8Array) => {
    while (v.length > 32 && v[0] === 0x00) v = v.slice(1);
    if (v.length > 32) return null;
    const p = new Uint8Array(32);
    p.set(v, 32 - v.length);
    return p;
  };
  const rp = fix(r), sp = fix(s);
  if (!rp || !sp) return null;
  const out = new Uint8Array(64);
  out.set(rp, 0);
  out.set(sp, 32);
  return out;
}

async function getKeys(force = false): Promise<Map<string, CryptoKey>> {
  if (!force && keyCache && Date.now() - keyCache.at < KEY_TTL_MS) return keyCache.keys;
  const res = await fetch(KEY_URL);
  if (!res.ok) throw new Error(`verifier keys ${res.status}`);
  const json = await res.json() as { keys: VerifierKey[] };
  const map = new Map<string, CryptoKey>();
  for (const k of json.keys) {
    const key = await crypto.subtle.importKey(
      "spki",
      b64ToBytes(k.base64),
      { name: "ECDSA", namedCurve: "P-256" },
      false,
      ["verify"],
    );
    map.set(String(k.keyId), key);
  }
  keyCache = { at: Date.now(), keys: map };
  return map;
}

Deno.serve(async (req) => {
  const url = new URL(req.url);
  const qs = url.search.startsWith("?") ? url.search.slice(1) : url.search;

  // The signed content is everything before "&signature=" - signature and key_id
  // are always the last two parameters, in that order.
  const sigAt = qs.indexOf("&signature=");
  if (sigAt < 0) return new Response("missing signature", { status: 400 });
  const signedContent = qs.slice(0, sigAt);

  // EVERY field that decides anything comes out of the SIGNED content, never the raw
  // URL. Reading them from url.searchParams would let anyone replay a genuine signed
  // callback with "&custom_data=<their uuid>" appended: the signature still verifies
  // (it covers only the prefix) and the reward is redirected. signature/key_id are the
  // exception by definition - they sit after the boundary.
  const signed = new URLSearchParams(signedContent);
  const signature = url.searchParams.get("signature") ?? "";
  const keyId = url.searchParams.get("key_id") ?? "";
  const userId = signed.get("custom_data") ?? "";
  const transactionId = signed.get("transaction_id") ?? "";
  const adUnit = signed.get("ad_unit") ?? "";

  if (!signature || !keyId || !transactionId) {
    return new Response("missing params", { status: 400 });
  }
  // No user means we cannot attribute the reward. Not an error on Google's side -
  // ack it so they stop retrying something that will never succeed.
  if (!userId) return new Response("no custom_data", { status: 200 });
  // A malformed id would blow up the uuid cast in the RPC, turning into a 500 that
  // Google retries forever. Reject the shape here and acknowledge instead.
  if (!UUID_RE.test(userId)) {
    console.warn("[ssv] custom_data is not a uuid", { transactionId });
    return new Response("bad custom_data", { status: 200 });
  }
  // Google signs callbacks for EVERY publisher with the same global key set, so a
  // valid signature only proves "some AdMob account", not "ours". Without this,
  // anyone could point their own ad unit's SSV URL here and mint grants against our
  // meter from ads served in their app.
  if (!ALLOWED_AD_UNITS.has(adUnit)) {
    console.warn("[ssv] rejected foreign ad_unit", { adUnit, transactionId });
    return new Response("unknown ad_unit", { status: 403 });
  }

  let ok = false;
  try {
    let keys = await getKeys();
    let key = keys.get(keyId);
    if (!key) {
      // Google rotates keys. A warm isolate holding a stale cache would answer 400 to
      // every callback until the TTL expired - and 4xx is final, so those rewards are
      // gone. Refetch once before giving up.
      keys = await getKeys(true);
      key = keys.get(keyId);
    }
    // Still unknown: 5xx so Google RETRIES rather than dropping a real reward.
    if (!key) {
      console.error("[ssv] unknown key_id after refresh", { keyId });
      return new Response("unknown key_id", { status: 500 });
    }
    const raw = derToRaw(b64ToBytes(signature));
    if (!raw) return new Response("bad signature encoding", { status: 400 });
    ok = await crypto.subtle.verify(
      { name: "ECDSA", hash: "SHA-256" },
      key,
      raw,
      new TextEncoder().encode(signedContent),
    );
  } catch (e) {
    console.error("[ssv] verification error", e);
    return new Response("verification error", { status: 500 });  // 5xx => Google retries
  }

  if (!ok) {
    console.warn("[ssv] REJECTED signature", { keyId, transactionId });
    return new Response("invalid signature", { status: 403 });
  }

  // Service role: grant_ad_refill_verified is deliberately not callable by clients.
  const supabase = createClient(
    Deno.env.get("SUPABASE_URL")!,
    Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!,
  );
  const { data, error } = await supabase.rpc("grant_ad_refill_verified", {
    p_user_id: userId,
    p_transaction_id: transactionId,
  });

  if (error) {
    console.error("[ssv] grant failed", error);
    return new Response("grant failed", { status: 500 });        // retryable
  }

  // A refusal (rate_limited, premium, attempts_full) is a final answer, not a
  // failure: 200 so Google stops retrying a callback that will never pay.
  console.log("[ssv] granted", { transactionId, result: data });
  return new Response(JSON.stringify(data), {
    status: 200,
    headers: { "content-type": "application/json" },
  });
});
