## 1. Build flat panel runtime data

- [ ] 1.1 Add bounded panel definitions, compilation into flat arrays, and independent instance-state tests.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Panel instances execute independent flat grids :: Two panels from one definition are independent -->
- [ ] 1.2 Validate definition and edit coordinates before runtime mutation.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Panel instances execute independent flat grids :: Invalid panel coordinate is rejected -->
- [ ] 1.3 Add stable standard-rule registrations and parameter schemas for the complete Stage 4 cell set.

## 2. Implement passive connectivity and observation

- [ ] 2.1 Add wire, junction, crossing, constant, and panel-port connectivity with orientation tests.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Standard cell set is available :: Crossing keeps channels separate -->
- [ ] 2.2 Add non-driving probes that record only committed tick-value pairs.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Standard cell set is available :: Probe records committed values -->
- [ ] 2.3 Add transport-delay route events and compare rising and falling edges across route lengths.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Spatial routes use transport delay :: Longer route delays both edges -->
- [ ] 2.4 Add the unequal-path XOR fixture and exact transient trace.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Spatial routes use transport delay :: Unequal XOR paths expose a glitch -->

## 3. Implement active standard cells

- [ ] 3.1 Add NAND pending-candidate state and preserve repeated-candidate deadlines.
<!-- covers: spatial-circuits/panels-and-standard-cells :: NAND uses exact inertial delay :: Repeated candidate keeps original deadline -->
- [ ] 3.2 Cancel a pending NAND output when the candidate returns to the committed value.
<!-- covers: spatial-circuits/panels-and-standard-cells :: NAND uses exact inertial delay :: Candidate returns to committed output -->
- [ ] 3.3 Replace a pending NAND candidate and restart its deadline from the change tick.
<!-- covers: spatial-circuits/panels-and-standard-cells :: NAND uses exact inertial delay :: Candidate changes before maturity -->
- [ ] 3.4 Add clock temporal state and DFF stable-data capture with default clock-to-output delay.
<!-- covers: spatial-circuits/panels-and-standard-cells :: DFF timing failures are deterministic :: Stable data is captured -->
- [ ] 3.5 Add deterministic `Unknown` output for same-tick DFF data and rising clock delivery.
<!-- covers: spatial-circuits/panels-and-standard-cells :: DFF timing failures are deterministic :: Same-tick data and clock violate setup -->
- [ ] 3.6 Add stability-filter candidate and consecutive-age state that rejects short glitches.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Stability filter requires consecutive candidate age :: Short glitch is rejected -->
- [ ] 3.7 Accept all four stable candidates exactly at the configured filter threshold.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Stability filter requires consecutive candidate age :: Stable candidate is accepted -->

## 4. Add topology safety and public fixtures

- [ ] 4.1 Route cell replacement through incarnations and prove old delayed outputs are ignored.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Cell replacement is incarnation safe :: Replaced cell ignores old delayed output -->
- [ ] 4.2 Add versioned golden traces for routes, XOR glitch, filtering, DFF setup failure, and replacement.
- [ ] 4.3 Add the public XOR practice command with filtered and unfiltered modes.
<!-- covers: spatial-circuits/panels-and-standard-cells :: Panel timing is runnable through the public runner :: XOR filtering practice is repeatable -->

## 5. Run Stage 4 gates

- [ ] 5.1 Run formatting, pure dependency checks, `dotnet build`, and all direct and routed panel tests.
- [ ] 5.2 Run the public XOR practice in both modes and record exact traces and process codes.
- [ ] 5.3 Run strict OpenSpec validation and `dod-guard cover` for this change before handoff.
