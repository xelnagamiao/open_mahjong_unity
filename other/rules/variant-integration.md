# Variant integration and isolated validation

This branch adds filipino to the existing Open Mahjong monorepo. The repository
contains the Python game server, Unity client, Vue/Node website and Go chat
server; a separate authoritative game-server repository is not required here.
The implementation was based on commit `520f0f3b003a582ebd51cebf33184a46959d4f69`.

## Architecture

The new rule owns its tile model, legal shape/scoring evaluator, player state,
explicit state machine, protocol adapter, recording adapter and bot. The design
uses Guobiao's separation between an active-player decision and a discard
response window: clients submit choices, and the server validates and resolves
priority before mutation. Settlement is server-owned and applied once. The
existing `ZhongyongGameState` shell supplies mature queues, spectator delivery,
record lifecycle and room cleanup; its scoring/turn implementation is overridden.
This intentional dependency avoids a second copy of the shared transport shell.

A turn passes through opening replacement, hand/discard-only decisions,
discard responses, hand end, ready barrier and the next round or final match.
Vietnamese also has a rob-kong and special-declaration window. Configurations
are versioned and copied into rooms, snapshots and records. Action ticks reject
stale responses; membership checks reject observers, queues reject duplicates,
and receipt deadlines reject late actions. Reconnect builds a private snapshot
and the current offered decision. Private hands are not sent to other seats.

Unity registers a `RuleManifest` and uses the existing `TableMirror`, generic
3D/2D managers, hand interaction, action buttons and result panels. Detailed room
options are baked into the existing MainScene create-room panels by an editor
baker, preserving serialized references. Record extensions are handled both by
the real C# replay code and the Python record simulator. Original tile faces are
provided for Vietnamese's additional flowers and jokers.

The server statistics catalog is the source for the generated Unity/web catalog.
The existing unified record/metrics writer stores this rule/subrule identity.
Basic statistics are enabled, fan-category statistics and matching/rating queues
are not. No schema-wide reset or production migration is needed for this change.
The web change is the statistics catalog; a new browser-playable rules engine
and an online rulebook page are not included. The existing Unity rulebook button
therefore needs the corresponding website documentation page before publication;
source documentation for this branch is in this directory.

## Reproduce server validation

Use an isolated checkout with the repository's Python dependencies installed.
From `open_mahjong_server`, use Python 3.12 and PowerShell:

```powershell
$env:PYTHONPATH = (Get-Location).Path + ';' + (Join-Path (Get-Location) 'tests/database')
$env:PYTHONIOENCODING = 'utf-8'
python -m pytest --import-mode=importlib -c server/pytest.ini server tests -q --junitxml=../.om_workspace/validation/server-full.xml
python -m pytest --import-mode=importlib -c server/pytest.ini server/game_calculation/filipino server/gamestate/game_filipino -q
python -m server.gamestate.verifier.check_contract
python -m server.gamestate.game_filipino.export_validation --output ../.om_workspace/validation/fixtures
```

The repository default full-suite command excludes the seven Guobiao heuristic
`selfplay` cases. Those are a separate lab workload, including 63 full matches;
they must not be described as passed by the command above. Database cases skip
without `TEST_DATABASE_DSN`. To run `tests/database/test_filipino_integration.py`,
set that variable through a private environment, using a disposable test DB
account allowed to create/drop schemas. The fixture creates and drops a unique
`subrule_test_<uuid>` schema, never the shared application schema. Do not commit
credentials. The test checks four record indexes, four metrics rows, empty
fan aggregates, identity/config validation and successful authenticated room creation.

The new `test_lifecycle.py` runs a complete four-wind match through the actual
async bot/action queues, ready barriers and finalization, and verifies one-time
persistence/cleanup. It uses the real presentation delays; Filipino can take
several minutes because 36 bonus tiles per hand generate replacement pauses.
`test_roundtrip.py` compares physical hands, wall, rivers, meld masks and scores
against the Python simulator over multiple hands and random seeks.

## Unity batch checks and builds

Use Unity **6000.4.7f1**, Windows x64, with the branch's own project directory and
physical `Library` directory. Never point batch Unity at an already-open project,
never junction its Library to the user's active Library. Preserve the project's
pinned Packages manifest/lock. Define `OPEN_MAHJONG_VALIDATION_OUTPUT` as an
absolute path under this checkout's `.om_workspace/validation`.

Run the following Unity methods separately, waiting for each process to exit:

```text
Unity.exe -batchmode -nographics -projectPath <branch>/open_mahjong_unity -executeMethod FilipinoValidation.RunBatch -logFile <branch>/.om_workspace/validation/unity-client.log
Unity.exe -batchmode -nographics -projectPath <branch>/open_mahjong_unity -executeMethod FilipinoValidation.BuildBatch -logFile <branch>/.om_workspace/validation/unity-build.log
```

The runner invokes all public optional-argument `Run` methods ending in
`Validation`; failures retain their exception in `unity-client-tests.json` and
return nonzero. This is the repository's custom batch-validation convention,
not an assertion that an NUnit EditMode/PlayMode suite ran. New checks cover the
manifest, room serialization/options, hand shapes, and real C# record mutations.
Do not erase existing unrelated failures from a handoff report.

BuildBatch creates a development Windows x64 player below the private validation
output directory. It temporarily uses company `OpenMahjongIsolatedValidation`
and product `FilipinoMahjongValidation`, isolating player preferences from the
installed client, then restores the project's settings. Launch hidden on Windows
with `Start-Process -WindowStyle Hidden`; retain output logs and exit codes.

The compiled player has two explicit opt-in checks:

```text
FilipinoMahjong.exe -batchmode -nographics --filipino-audit <fixtures>/filipino-0.json <output>/native-replay.json -logFile <output>/native-replay.log
FilipinoMahjong.exe -batchmode --filipino-table-smoke <fixtures>/table-smoke.json <output>/table-smoke -logFile <output>/table-smoke.log
```

Replay audit runs the same C# mutation code in the player. Table smoke routes
server snapshots through `GameStateNetworkManager` into the actual scene,
asserts TableMirror data and renders the real URP camera/canvases into a private
target. The script requires nonblank pixels; inspect PNGs as well. Its test
transport is loopback-only, and it never logs into shared services. The fixture
is offline: it is not a four-client live network end-to-end test.

## Other monorepo checks and limits

Install web root/client dependencies using their lockfiles. The Game2D tests
require the repository's `client/scripts/test-game2d.mjs` bundling harness;
plain Node cannot import their Vite font assets or `import.meta.env` values.
Other Node tests need a TypeScript loader where applicable, and placeholder
ADMIN/BOT_API environment settings for isolated HTTP tests. Supply secrets only
through a private environment; never use deployment values. Run web tests
sequentially to avoid competing Vite HMR test ports, and run `npm run build`
from `open_mahjong_web/client`. Optional external database tests may skip.

`go test ./...` in the chat project requires a local ignored `secret_key.txt`;
use a disposable test-only value in the private checkout and remove that exact
file after the run. Do not change the shared chat service's key.

These checks cover finite scripted scenarios. Human input, latency/loss,
multiple live Unity clients, mobile/WebGL/Steam publication, localization polish,
and long-term load/performance remain separate acceptance work. Neither branch
should be merged or published solely because its compilation succeeds.
