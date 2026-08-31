# OyinQ manual verification

Run against an approved staging PostgreSQL database and real Telegram groups.

1. Open an existing Club as a member; confirm there is no registration gate.
2. Create Club and Camp communities with Telegram native chat selection; reject a reused chat.
3. Add an administrator with native user selection; confirm there is no raw-ID field.
4. Browse/search the Club collection and verify Russian type/category labels on cards, details, and pickers.
5. Add a BGG game with expansions, export JSON, re-import it, and verify stale revision conflict handling.
6. Search BGG by exact name, prefix, whole word, and substring; verify ranked lightweight results, years, and “Показать ещё”.
7. Create a Club gathering from the collection and another from a BGG link; confirm neither mutates the legacy global catalog.
8. Create a Camp from a Club; change the Club and confirm the Camp base snapshot stays unchanged.
9. Activate the Camp and register with city, accommodation choice, and several exact dates.
10. Reopen registration through “Редактировать регистрацию”; confirm saved exact dates are restored.
11. Try to create or join a Camp gathering on an unselected date; verify rejection, then select the date and retry.
12. As an organizer, try to remove a date containing an active future gathering; verify the edit is blocked.
13. As a participant, remove such a date; verify the affected-gathering summary, explicit confirmation, withdrawal, and one ordered promotion.
14. Cancel Camp registration; verify contributions/import work/future participation are removed, while history and other communities remain.
15. Start a large BGG import, close/reopen the Mini App, and verify the persisted job continues.
16. In import selection, test expansion-without-base warning and explicitly add a personal copy of a base-library duplicate.
17. Try to add a base-library duplicate manually; verify both UI and backend reject it.
18. Verify a base-library-only game has no personal “Точно привезу” prompt; an explicit personal duplicate does.
19. Have two participants contribute one game; verify one catalog item, both providers, and clickable username profiles where public.
20. Verify users without a public username remain readable non-links in provider and gathering participant lists.
21. Create a gathering with an empty description; confirm taxonomy is shown separately and not copied into description.
22. Fill a gathering, create an ordered waitlist, leave a confirmed place, and verify exactly one promotion plus best-effort DM.
23. Close/reopen/edit a gathering; reject a maximum below confirmed count and unsafe expansion selection.
24. Manually cancel a gathering and simulate Telegram publication failure; verify the row remains in History → Отменены with retryable publication state.
25. Let an underfilled gathering pass its start; verify the distinct hard-delete cleanup path, not cancellation history.
26. Inspect the group announcement: organizer/participants are escaped direct mentions and the same message is edited in place.
27. Test light/dark themes, narrow safe-area layout, responsive desktop layout, and fullscreen enter/exit labels on supported Telegram clients.
28. Restart once in webhook mode and once in Development long-polling mode; confirm structured startup logs report five private commands, one group command, and the private Mini App menu URL without exposing the token.
29. In a managed group type `/`; verify only `/oyinq — Открыть OyinQ` is suggested. Run it and verify the response names the correct Club/Camp and opens the contextual flow through the bot DM.
30. In an unmanaged group run `/oyinq` as a normal user and as a global administrator. Verify only the administrator sees an admin entry action and no community is created.
31. In a private chat verify `/start`, `/menu`, `/help`, `/privacy`, and authorization-checked `/admin`; verify `/privacy` opens the production `/privacy` URL in a normal browser without Telegram `initData`.
32. Create one new Club and one new Camp through peer selection. Verify each group gets exactly one onboarding notice. Simulate send failure and verify creation remains committed and the administrator sees the warning. Restart and verify no notice repeats.
33. Confirm managed groups have no persistent reply keyboard or permanent Open button. Verify native peer-selector fallback keyboards remain private and are removed after use.
34. Complete the manual Main Mini App, previews, privacy URL, description picture, and splash checklist in `docs/botfather-setup.md`; test the bot profile Open App action in current Telegram mobile and desktop clients.
