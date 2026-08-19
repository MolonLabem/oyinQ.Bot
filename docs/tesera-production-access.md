# Tesera production access

## Current production diagnosis

`GET /health/tesera` performs a real read through the same `ITeseraClient` used by imports. On the current Northflank deployment, Tesera answers the bot host with DDoS-Guard HTTP 403.

The same target-side 403 was reproduced through an independent public reader for both the Tesera API and public Tesera pages. This rules out the JSON parser, collection pagination, Telegram flow, and the bot's request headers as the primary cause. The failure is network/IP filtering before the Tesera application serves the requested data.

Do not add random public proxies or HTML scraping as a production workaround. They are unreliable, make collection completeness impossible to guarantee, and can turn a provider outage into silent data corruption.

## Application behaviour

The bot now treats Tesera as a live dependency:

- a background monitor probes Tesera through `ITeseraClient`;
- healthy results are cached for 5 minutes;
- unavailable results are cached for 2 minutes;
- each probe has a 5-second timeout;
- Tesera import buttons are hidden while the provider is unavailable;
- stale Tesera callbacks fail immediately instead of enqueuing a doomed import;
- `/health/tesera` returns a stable reason such as `http_403`, `http_401`, or `timeout` without exposing provider exception text;
- when access recovers, Tesera returns to the menu automatically without a redeploy.

## Recommended Northflank change

Northflank managed cloud uses shared, unpredictable outbound IPs by default. Create a dedicated egress IP for the OyinQ service so all requests to Tesera originate from one stable address.

In Northflank:

1. Open the team menu and go to `Cloud → Egress IPs`.
2. Create an egress IP.
3. Choose `Dedicated` provisioning.
4. Choose the same region as the OyinQ workload.
5. Set mode to `Include`.
6. Add the OyinQ project and enable restrictions.
7. Select only the `oyinqbot` service.
8. Create the egress IP and wait until its state is `Active`.
9. Record the assigned public IP.
10. Re-check `https://p01--oyinqbot--668p7wnqfhrf.code.run/health/tesera`.

If Tesera still returns `http_403`, ask Tesera to allowlist that exact static IP. A static address makes that request actionable; the current shared Northflank address cannot be safely allowlisted because it is not stable.

Northflank documentation: `https://northflank.com/docs/v1/application/network/configure-egress-ips`.

## Verification after the egress change

Expected dependency response:

```json
{
  "dependency": "tesera",
  "status": "ok",
  "reason": "ok"
}
```

Then verify in Telegram:

1. Open `Профиль → Мои игры → Импорт коллекции`.
2. Confirm `Коллекция Tesera` is visible.
3. Import a small known Tesera collection.
4. Confirm only base games are added.
5. Confirm the completion message contains added/skipped counts.

If `/health/tesera` remains `http_403`, do not re-enable or force the import from the UI. Resolve the provider allowlist/network path first.
