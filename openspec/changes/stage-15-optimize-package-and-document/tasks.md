## 1. Record reference baselines

- [ ] 1.1 Add versioned benchmark projects and fixture sets without changing reference execution.
- [ ] 1.2 Record fixture hashes, cell count, frontier size, throughput, allocation, frame time, mode, build, and machine context.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Performance claims use versioned measurements :: Release benchmark is recorded -->

## 2. Apply measured optimizations

- [ ] 2.1 Select one measured bottleneck and add a separately selectable optimized path.
- [ ] 2.2 Compare the complete qualified trace and hash set between reference and optimized modes.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Optimizations preserve canonical behavior :: Optimized and reference runs agree -->
- [ ] 2.3 Fail qualification at the first differing tick and stable snapshot path.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Optimizations preserve canonical behavior :: Optimization diverges -->
- [ ] 2.4 Add long-quiet fixtures for clocks, timers, filters, delayed gates, cable queues, and topology work.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Quiet periods preserve temporal roots :: Clock follows a long quiet interval -->

## 3. Add safe execution-mode policy

- [ ] 3.1 Centralize main-thread and worker admission using active recorded device and cell counts.
- [ ] 3.2 Safely reject or barrier a topology edit that activates recorded Node behavior during worker execution.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Worker mode has strict eligibility :: Recorded Node becomes active -->

## 4. Build and verify the release package

- [ ] 4.1 Extend staging to produce the final pure core and self-contained addon with version and path-hash manifests.
- [ ] 4.2 Verify every packaged path and reject repository-relative runtime dependencies.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Release packages are self-contained and identified :: Package manifest is verified -->
- [ ] 4.3 Add bounded local Windows release commands for managed checks, Godot import and build, acceptance, export, and blank-consumer installation.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Windows release gates exercise public surfaces :: Windows release gates pass -->
- [ ] 4.4 Preserve diagnostic output and fail on any nonzero exit, timeout, or acceptance-hash mismatch.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Windows release gates exercise public surfaces :: One release process fails -->
- [ ] 4.5 Make Windows CI run the same local release commands and retain bounded artifacts.

## 5. Document and practice the release

- [ ] 5.1 Add maintained installation, architecture, extension, save, replay, diagnostics, performance, and qualification documentation.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Documentation states supported and unqualified surfaces :: User checks platform support -->
- [ ] 5.2 Add the public blank-project installation and two-device timed-exchange practice.
<!-- covers: spatial-circuits/release-performance-and-packaging :: Final package practice works outside the demo :: Blank-project release practice completes -->

## 6. Run Stage 15 gates

- [ ] 6.1 Run formatting, dependency checks, `dotnet build`, all tests, benchmark correctness comparisons, and package verification.
- [ ] 6.2 Run the full bounded Windows release suite and retain exact process results and acceptance hashes.
- [ ] 6.3 Run the final blank-project practice and record the package manifest and timed trace.
- [ ] 6.4 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
