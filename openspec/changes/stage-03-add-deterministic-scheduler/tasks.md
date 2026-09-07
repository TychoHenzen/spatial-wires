## 1. Add canonical scheduling data

- [ ] 1.1 Add the full scheduled-event key, one canonical comparer, and shuffled-insertion hash tests.
<!-- covers: spatial-circuits/deterministic-scheduler :: Scheduled work has canonical total order :: Shuffled insertion produces the same trace -->
- [ ] 1.2 Add same-target, distinct-port delivery cases to the event identity and queue tests.
<!-- covers: spatial-circuits/deterministic-scheduler :: Scheduled work has canonical total order :: Distinct target ports remain distinct -->
- [ ] 1.3 Add accepted external command ordinals and same-target acceptance-order tests.
<!-- covers: spatial-circuits/deterministic-scheduler :: External and internal causality receives deterministic ordinals :: Same-target commands follow acceptance order -->
- [ ] 1.4 Canonically reduce internal proposals before assigning persisted causal ordinals.
<!-- covers: spatial-circuits/deterministic-scheduler :: External and internal causality receives deterministic ordinals :: Internal proposal order does not depend on enumeration -->

## 2. Implement the microtick coordinator

- [ ] 2.1 Add explicit phase state and prove evaluation reads one fully resolved committed snapshot.
<!-- covers: spatial-circuits/deterministic-scheduler :: Microticks commit through fixed phases :: Evaluation reads one committed snapshot -->
- [ ] 2.2 Buffer evaluation effects for `T + 1` or later and prove current evaluation inputs remain immutable.
<!-- covers: spatial-circuits/deterministic-scheduler :: Microticks commit through fixed phases :: Effects do not mutate the current evaluation snapshot -->
- [ ] 2.3 Reject current-tick and past proposals with stable diagnostics.
<!-- covers: spatial-circuits/deterministic-scheduler :: Causal operations have positive delay and no re-entry :: Zero-delay proposal is rejected -->
- [ ] 2.4 Add the non-reentrant world-step guard and nested-step failure tests.
<!-- covers: spatial-circuits/deterministic-scheduler :: Causal operations have positive delay and no re-entry :: Callback attempts scheduler re-entry -->

## 3. Add persistent world state

- [ ] 3.1 Add source-indexed persistent drive tables and prove unchanged drives persist.
<!-- covers: spatial-circuits/deterministic-scheduler :: Persistent drives resolve independently of update order :: Unchanged source drive persists -->
- [ ] 3.2 Add source-specific release handling without removing other drives.
<!-- covers: spatial-circuits/deterministic-scheduler :: Persistent drives resolve independently of update order :: Source release removes only that source -->
- [ ] 3.3 Add target incarnations and reject delivery to a replacement at the reused stable location.
<!-- covers: spatial-circuits/deterministic-scheduler :: Incarnations invalidate stale work :: Replacement does not receive stale event -->
- [ ] 3.4 Generate deterministic release drives during target removal.
<!-- covers: spatial-circuits/deterministic-scheduler :: Incarnations invalidate stale work :: Removal releases persistent drives -->
- [ ] 3.5 Track future temporal obligations and prove a timer fires after quiet microticks.
<!-- covers: spatial-circuits/deterministic-scheduler :: Temporal roots remain schedulable without new input :: Timer fires after quiet microticks -->

## 4. Add snapshots, hashing, and replay

- [ ] 4.1 Add immutable end-of-tick snapshots with counters, incarnations, drives, events, cancellations, roots, and trace cursor.
- [ ] 4.2 Refuse or defer snapshot requests made before the end-of-tick boundary.
<!-- covers: spatial-circuits/deterministic-scheduler :: End-of-tick snapshots restore deterministic execution :: Snapshot is requested during a microtick -->
- [ ] 4.3 Add restore, command replay, and canonical per-tick hashing.
- [ ] 4.4 Add the public ten-microtick snapshot fixture and prove restored hashes match.
<!-- covers: spatial-circuits/deterministic-scheduler :: End-of-tick snapshots restore deterministic execution :: Ten-microtick snapshot practice reproduces hashes -->

## 5. Run Stage 3 gates

- [ ] 5.1 Run formatting, pure dependency checks, `dotnet build`, and scheduler tests with randomized insertion seeds.
- [ ] 5.2 Run the public snapshot, restore, and replay practice command and record the hash sequence.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
