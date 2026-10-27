import { credentialResponse, methodNotAllowed } from "./credential.js";

export default {
    async fetch(request, env) {
        const url = new URL(request.url);
        if (url.pathname === "/api/danmaku/credential") {
            if (request.method !== "GET") return methodNotAllowed();
            return credentialResponse(env);
        }
        return env.ASSETS.fetch(request);
    }
};
