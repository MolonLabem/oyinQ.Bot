# Mini App entry-page caching

`MiniAppStaticFiles.Options()` applies `Cache-Control: no-cache` to the Mini App entry document through both static-file middleware and the SPA fallback. This includes `/app/`, `/app/index.html`, query-string launch contexts and nested client routes. Normal network loads must revalidate the document before reusing it; ETag/Last-Modified conditional responses remain available. Vite's content-hashed bundles retain normal static-file caching.

Existing Telegram buttons carry application context, not a frontend release version. Keep those URLs compatible; replacing buttons is not an update mechanism.

This policy takes effect after deployment and receipt of a new response. It cannot evict previously cached responses remotely or reload an already running/minimized Telegram WebView. An affected user may need one manual refresh. Reopening a retained WebView is not necessarily a new document request. Do not force-reload active forms or clear participant local storage to fix asset caching.

Verification: HTTP tests cover direct/default/fallback documents, launch query strings, conditional requests, and unchanged bundle caching. Live Telegram client retention and any deployment proxy cache must be verified separately.
