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

async function getKeys(): Promise<Map<string, CryptoKey>> {
  if (keyCache && Date.now() - keyCache.at < KEY_TTL_MS) return keyCache.keys;
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

  const params = url.searchParams;
  const signature = params.get("signature") ?? "";
  const keyId = params.get("key_id") ?? "";
  const userId = params.get("custom_data") ?? "";
  const transactionId = params.get("transaction_id") ?? "";

  if (!signature || !keyId || !transactionId) {
    return new Response("missing params", { status: 400 });
  }
  // No user means we cannot attribute the reward. Not an error on Google's side -
  // ack it so they stop retrying something that will never succeed.
  if (!userId) return new Response("no custom_data", { status: 200 });

  let ok = false;
  try {
    const keys = await getKeys();
    const key = keys.get(keyId);
    if (!key) return new Response("unknown key_id", { status: 400 });
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
