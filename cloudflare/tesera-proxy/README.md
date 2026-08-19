# OyinQ Tesera proxy

Small authenticated Cloudflare Worker used only to relay the Tesera API calls required by OyinQ.

It is intentionally not a general-purpose proxy:

- GET requests only;
- requires `Authorization: Bearer <PROXY_TOKEN>`;
- allows only `/games/{alias}` and the collection paths used by `TeseraClient`;
- always forwards to `https://api.tesera.ru`;
- does not forward arbitrary client headers;
- does not cache Tesera responses.

## Deploy

Requirements: Node.js and a Cloudflare account with Workers enabled.

```bash
cd cloudflare/tesera-proxy
npm install
npx wrangler login
```

Generate a random shared secret, for example:

```bash
openssl rand -hex 32
```

Store the generated value as the Worker secret:

```bash
npx wrangler secret put PROXY_TOKEN
```

Paste the value when Wrangler prompts for it, then deploy:

```bash
npm run deploy
```

Wrangler prints a URL similar to:

```text
https://oyinq-tesera-proxy.<your-subdomain>.workers.dev
```

## Verify before changing Northflank

```bash
curl -i \
  -H 'Authorization: Bearer <PROXY_TOKEN>' \
  'https://oyinq-tesera-proxy.<your-subdomain>.workers.dev/games/carcassonne'
```

The important result is HTTP 200 with Tesera JSON. If the Worker itself receives HTTP 403 from Tesera, do not configure OyinQ to use it; Cloudflare egress is also blocked and this approach will not solve the provider restriction.

An unauthenticated request should return 404:

```bash
curl -i 'https://oyinq-tesera-proxy.<your-subdomain>.workers.dev/games/carcassonne'
```

## Configure OyinQ on Northflank

Only after the authenticated Worker probe returns HTTP 200, add these runtime environment variables to the OyinQ service:

```text
TESERA_BASE_URL=https://oyinq-tesera-proxy.<your-subdomain>.workers.dev
TESERA_PROXY_TOKEN=<same PROXY_TOKEN value>
```

Redeploy OyinQ, then verify:

```text
GET https://p01--oyinqbot--668p7wnqfhrf.code.run/health/tesera
```

Expected result:

```json
{
  "dependency": "tesera",
  "status": "ok",
  "reason": "ok"
}
```

Do not commit `PROXY_TOKEN` to GitHub or put it in `wrangler.jsonc`.
