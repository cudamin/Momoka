import assert from "node:assert/strict";
import test from "node:test";
import { credentialResponse, encryptCredential } from "./credential.js";
import worker from "./index.js";

const key = "yYvsbrP9o55bxzdEeh8kQVD9Gf25+rjX/BIndmDBcd8=";
const env = {
    DANDANPLAY_APP_ID: "fixture-app",
    DANDANPLAY_APP_SECRET: "fixture-secret",
    DANDANPLAY_CREDENTIAL_KEY: key,
    DANDANPLAY_CREDENTIAL_KID: "fixture-kid"
};

async function decrypt(response, expectedKid, expectedExp) {
    const data = Uint8Array.from(Buffer.from(response.data, "base64"));
    const imported = await crypto.subtle.importKey("raw", Uint8Array.from(Buffer.from(key, "base64")),
        "AES-GCM", false, ["decrypt"]);
    const plaintext = await crypto.subtle.decrypt({
        name: "AES-GCM",
        iv: data.subarray(0, 12),
        additionalData: new TextEncoder().encode(`momoka-danmaku-credential-v1|${expectedKid}|${expectedExp}`),
        tagLength: 128
    }, imported, data.subarray(12));
    return JSON.parse(new TextDecoder().decode(plaintext));
}

test("凭据端点：AES-GCM 往返并绑定 kid 与 exp", async () => {
    const now = 1_790_000_000;
    const response = await credentialResponse(env, now);
    assert.equal(response.status, 200);
    assert.equal(response.headers.get("cache-control"), "no-store");
    const body = await response.json();
    assert.equal(body.v, 1);
    assert.equal(body.kid, "fixture-kid");
    assert.equal(body.exp, now + 7 * 24 * 60 * 60);
    assert.deepEqual(await decrypt(body, body.kid, body.exp),
        { appId: "fixture-app", appSecret: "fixture-secret", exp: body.exp });
});

test("凭据端点：未配置环境变量时拒绝", async () => {
    const response = await credentialResponse({}, 1_790_000_000);
    assert.equal(response.status, 503);
    assert.equal((await response.json()).error, "credential endpoint is not configured");
});

test("凭据端点：错误密钥无法解密", async () => {
    const payload = await encryptCredential("fixture-app", "fixture-secret",
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=", "fixture-kid", 1_790_000_000);
    await assert.rejects(() => decrypt(payload, payload.kid, payload.exp));
});

test("Worker：只接受 GET，静态资源回退给 ASSETS", async () => {
    const post = await worker.fetch(new Request("https://momoka.test/api/danmaku/credential", { method: "POST" }),
        { ASSETS: { fetch: () => new Response("asset") } });
    assert.equal(post.status, 405);

    const get = await worker.fetch(new Request("https://momoka.test/api/danmaku/credential"), env);
    assert.equal(get.status, 200);

    const page = await worker.fetch(new Request("https://momoka.test/index.html"),
        { ...env, ASSETS: { fetch: () => new Response("asset") } });
    assert.equal(await page.text(), "asset");
});
