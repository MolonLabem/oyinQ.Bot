# OyinQ Tesera proxy

This Worker is a narrow authenticated egress proxy for the Tesera endpoints used by OyinQ. It accepts only `GET` requests for supported collection and game-detail paths and always targets `https://api.tesera.ru`.

## Local verification

```bash
npm install
npm run types
npm run check
```

For local `wrangler dev`, create an untracked `.dev.vars` file containing `TESERA_PROXY_SECRET=...`.

## Deployment

Set the production secret interactively, then deploy:

```bash
npx wrangler secret put TESERA_PROXY_SECRET
npx wrangler deploy
```

Configure the same value as `TESERA_PROXY_SECRET` on the OyinQ host and set `TESERA_PROXY_BASE_URL` to the deployed Worker origin. Never put the secret in this repository or in `wrangler.jsonc`.
