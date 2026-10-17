# Hongque server tests

This directory contains the Hongque runtime modules together with their unit and
behavioral regression tests.

Run from `open_mahjong_server` using the project's virtual environment:

```powershell
& .\.venv\Scripts\python.exe -m pytest server/gamestate/game_hongque -q
```

## Reconnect and lifecycle regression

- `test_hongque_reconnect.py`: all four seats in turn, claim, tactical recheck,
  only-cut, supplement, kong, self draw, ron, multi-ron, draw and final ranking.
  Replay fixtures also verify that initial hands and wall contain exactly one
  complete 126-tile deck, including the fixed tactical debug scenario.
- `test_hongque_transport.py`: four real loopback WebSocket peers, connection
  loss, fresh restore and actions through the production gamestate router.
  Fixture seat assignment replaces account authentication; no shared server or DB is changed.
- `test_hongque_lifecycle.py`: complete 8/16-hand main-loop matches, final records,
  invalid actions, all state-machine edges, cancellation and cleanup failures.
- `test_hongque_timeouts.py`: disconnected turn and claim deadlines, readiness races,
  stale bot decisions and decision-failure recovery.
- `test_hongque_pause.py`: turn/claim/tactical clock preservation, fresh actions and
  bot decisions after resume, and prevention of next-round dealing during pause
  or an in-flight resume broadcast.

Export the authoritative packets and replay fixtures for the opt-in Unity runtime test:

```powershell
& .\.venv\Scripts\python.exe -m server.gamestate.game_hongque.test_hongque_reconnect --output ../.om_workspace/hongque-client-validation
```

Build a Development Player containing MainScene and run it with
`-batchmode -nographics --verify-hongque-client <absolute-output-directory>`.
Run again with `<absolute-output-directory>/replay-run` for replay and delayed spectating.
Use `<absolute-output-directory>/normal-time-run` for seven normal-speed restore
and settlement scenarios.
The test drives the real MainScene packet router, hand/river animations, settlement
panels and replay manager, including opening draw slots, waiting tiles, ron danger
and delayed-spectator catch-up after settlement animations from all four perspectives.
It checks physical hand objects and exact remaining wall tiles, treats pool exhaustion
as a failure, and records the duration of each case. It writes `runtime-results.json` and exits with code 0
only when every assertion passes. Animations default to 8× time scale; set a
fixture's `timeScale` to `1` to validate normal animation timing. Normal launches
do not enable the harness; non-development players exclude it.

Unity's `HongqueRecordTipsValidation.Run()` additionally covers variable hand sizes,
explicit draw-slot normalization, extended meld visibility, unique-tile supply and
unchanged ordinary-mahjong normalization, cached-query isolation and kong-only
self-draw hints. `HepaiRiverSourceValidation.Run()` verifies the physical river
source and later winners' presentation copies in both ordinary and expanded replay.
