# OyinQ manual verification

Run this checklist against a disposable or approved staging database and real Telegram groups.

1. Open an existing Club as a member and confirm no registration is requested.
2. Create a Club with Telegram's native chat picker.
3. Try the same chat again and confirm the collision is rejected.
4. Add an administrator with Telegram's native user picker; confirm no raw-ID field appears.
5. Have the selected administrator start OyinQ and open `/admin`.
6. Browse and search the Club collection.
7. Add a BGG game and selected expansions.
8. Export the Club JSON as a file.
9. Import that JSON and verify revision handling; test a stale screen receives 409.
10. Create a Club gathering from the collection.
11. Create a Club gathering from an arbitrary BGG link.
12. Verify the arbitrary game changed neither the Club collection nor the legacy global catalog.
13. Create a Camp with Telegram's native chat picker.
14. Confirm a Club chat cannot be reused as a Camp.
15. Create a Camp from a Club and inspect the base snapshot.
16. Edit the Club and confirm the Camp snapshot is unchanged.
17. Activate the Camp and complete participant registration within its inclusive duration.
18. Start a large personal BGG import and confirm the request returns immediately.
19. Close and reopen the Mini App; confirm the persisted import continues.
20. Confirm every imported item is initially selected.
21. Select an expansion without its base and confirm the yellow warning.
22. Confirm the selection.
23. Re-import after removing a BGG-owned item; confirm stale BGG-source rows disappear and manual rows remain.
24. Have two participants contribute the same game and confirm one catalog item shows both providers/copies.
25. Create a Camp gathering inside the Camp date range; verify an outside date is rejected.
26. Join the gathering and confirm only the leave action remains.
27. Fill it and verify ordered waitlisting.
28. Leave a confirmed place and verify exactly one promotion.
29. Confirm the promoted participant receives a DM; DM failure must not undo promotion.
30. Edit time, limits, description, rules, and safe expansion selection as organizer; reject a maximum below confirmed count.
31. Close, reopen, and cancel a gathering; confirm the announcement edits in place.
32. Close the Camp and confirm registrations, imports, contributions, gatherings, joins, and leaves are blocked while admin history remains readable.
33. Test every primary screen in Telegram dark theme.
34. Test every primary screen in Telegram light theme and on a narrow device with safe areas.
35. Remove the administrator from all managed groups and confirm `/admin` still opens the global admin Mini App.
