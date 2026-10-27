const VERSION = 1;
const AAD_PREFIX = "momoka-danmaku-credential-v1|";
const LIFETIME_SECONDS = 7 * 24 * 60 * 60;

function bytesToBase64(bytes) {
    return btoa(String.fromCharCode(...bytes));
}

function base64ToBytes(value) {
    return Uint8Array.from(atob(value), character => character.charCodeAt(0));
}

async function importKey(keyBase64) {
    const raw = base64ToBytes(keyBase64);
    if (raw.length !== 32) throw new Error("credential key must be 32 bytes");
    return crypto.subtle.importKey("raw", raw, "AES-GCM", false, ["encrypt"]);
}

function json(status, body) {
    return new Response(JSON.stringify(body), {
        status,
        headers: {
            "content-type": "application/json; charset=utf-8",
            "cache-control": "no-store"
        }
    });
}

export async function encryptCredential(appId, appSecret, keyBase64, kid, exp) {
    const key = await importKey(keyBase64);
    const nonce = crypto.getRandomValues(new Uint8Array(12));
    const plaintext = new TextEncoder().encode(JSON.stringify({ appId, appSecret, exp }));
    const aad = new TextEncoder().encode(AAD_PREFIX + kid + "|" + exp);
    const encrypted = new Uint8Array(await crypto.subtle.encrypt(
        { name: "AES-GCM", iv: nonce, additionalData: aad, tagLength: 128 }, key, plaintext));
    const data = new Uint8Array(nonce.length + encrypted.length);
    data.set(nonce);
    data.set(encrypted, nonce.length);
    return { v: VERSION, kid, exp, data: bytesToBase64(data) };
}

export async function credentialResponse(env, nowSeconds = Math.floor(Date.now() / 1000)) {
    const appId = (env.DANDANPLAY_APP_ID || "").trim();
    const appSecret = (env.DANDANPLAY_APP_SECRET || "").trim();
    const key = (env.DANDANPLAY_CREDENTIAL_KEY || "").trim();
    const kid = (env.DANDANPLAY_CREDENTIAL_KID || "").trim();
    if (!appId || !appSecret || !key || !kid) return json(503, { error: "credential endpoint is not configured" });

    const exp = nowSeconds + LIFETIME_SECONDS;
    return json(200, await encryptCredential(appId, appSecret, key, kid, exp));
}

export function methodNotAllowed() {
    return new Response("Method Not Allowed", { status: 405, headers: { allow: "GET", "cache-control": "no-store" } });
}
