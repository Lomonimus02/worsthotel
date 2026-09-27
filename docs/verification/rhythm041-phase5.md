# Prototype 0.4.1 phase 5 — automatic room sales

Status: phase gate passed. Full EditMode 778/778 and physical PlayMode 5/5. Fresh executable integration remains phase 9.

Production now opens four of six rooms for ordinary timed sales at an initial rate of 180. Eight existing deterministic enquiries per arrival date decide on the hotel clock: first-day enquiries 08:30–12:00, subsequent dates on the preceding afternoon 16:00–19:30. Available advertised rooms compete on price; configured demand and a stable enquiry roll determine conversion. No click or menu refresh creates a reservation, and a consumed enquiry never rerolls.

The reception ledger exposes room sales/rates and confirmed today/tomorrow stays. Closing a room or editing its asking rate preserves existing agreements. A confirmed future stay can move to a legally available room, including one closed to new sales, without changing its identity, dates or price. The real guest, key, room preparation and departure pipeline remains in use. Price drafts retain their original revision until explicit refresh so one staff member cannot silently overwrite another's change.

Schema 14 and LAN protocol 16 replicate bounded policy rows, consumed demand cursors, settings and automatic origin. Validation happens before state restoration. Client mirrors cannot sell or advance the authoritative clock. Ordinary acceptance and agreed-price editing commands are refused in production automatic mode. Revision limits retain room for actual arrival, relocation and checkout transitions.

## Evidence and corrections

- First focused EditMode run: **99/100 passed**, 1.5765888 seconds. `Logs/rhythm041-phase5-focused-attempt1.xml`. The new wire round-trip fixture omitted living-guest settings, then advanced into real arrivals; snapshot validation correctly refused this inconsistent guest mode. The fixture now uses production living systems with a labelled certainty-of-demand override. No snapshot validation was relaxed.
- Before physical verification, corrected two test expectations to use the configured 10-unit price step. Static review also found that the LAN maintenance diagnostic's single Back press returned to Overview rather than closing the journal; the diagnostic now selects the actual close option and waits for both local and authoritative UI blocking to clear before its physical hold.
- Full EditMode: **778/778 passed**, 188.1904792 seconds, `Logs/rhythm041-phase5-editmode.xml`. Includes the corrected wire round trip, invalid-policy/cursor/origin/revision atomic rejection, deterministic demand/price/availability and command validation, plus existing model/economy regressions.
- PlayMode: **5/5 passed**, 64.3146201 seconds, first attempt, `Logs/rhythm041-phase5-playmode.xml`. Controller operations pages 9.383421s; real automatic-reservation forecast/reassignment 13.247536s; session mirror through midnight 1.459120s; physical reception sales → timed automatic contract → fixed-price reassignment → actual rack key handoff → room106 arrival 39.369033s; explicit legacy host fixture compatibility 0.813076s. The physical case uses a labelled supply setup and bounded diagnostic clock advance, then genuine controller interaction and guest routes; it is not a natural full-day pacing claim.

## Scope of executable drivers

The three-day SOLO driver now observes actual automatically sold identities/counts instead of prescribing fourteen guests. It opens four rooms, then five for later enquiries, and closes new sales on day 3 while preserving all existing contracts. Its report checks every sold guest's physical room/departure route and exactly one receipt, unchanged rates and cash conservation. Its timed staff key/linen/assignment/repair operations remain explicitly labelled model adapters; it does not establish human pacing.

The two-process LAN driver now exercises remote policy editing, unchanged reservation count on policy edits, scheduled automatic conversion, stale policy and reservation rejection, fixed-price reassignment, cancellation and midnight persistence. Existing physical preventive maintenance and capital checks remain. The UI driver adds real sales/rate/reassignment screens and twenty expected capture candidates.

These source migrations are **not executable PASS results**. Fresh SOLO, LAN, visual and packaged-binary gates remain phase 9. Previous 0.4 results do not validate the new binary. The prescribed manual-booking economic matrix intentionally disables only automatic sales; its measured infrastructure results are not a claim about actual automatic occupancy or sales economics.
