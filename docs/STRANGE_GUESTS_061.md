# 0.6.1 — Strange guests & special stays

The requested Prototype 0.6 milestone follows the already released 0.6.0 interface pass, so its Windows build uses 0.6.1.

## Playing

Open the physical **RESERVATIONS** book at reception and choose **Special enquiries**. The first letter arrives at **10:00 on day 2**, for an arrival at **16:00 on day 3**. Subsequent opportunities appear every two days, rotating musician, overpacker and night owl. There is at most one unanswered enquiry. Ignoring it until arrival expires it; declining does not generate a replacement that day. Ordinary room sales remain automatic.

Accept by choosing an available room. Restored rooms 107–110 work too; opening them to ordinary sales is not required for an explicit special reservation. More rooms are on the next page. The stay lasts one night, with the usual 10:00 checkout. Payment is collected by the existing checkout/evaluation system, so the quoted premium is subject to normal compensation and satisfaction rules.

| Guest | Offered room charge | What arrives / changes |
| --- | ---: | --- |
| Elliot Barnes, touring musician | $420 | Suitcase, 12 kg instrument case, 10 kg amplifier. Two short rehearsal slots among normal rest, an outing and sleep. |
| Florence Bell, overpacker | $480 | Six physical suitcases. One agreement to handle the whole batch. |
| Robin Vale, night owl | $360 | One suitcase; outing until about 01:45, television and shower before sleep at 04:15, waking at 09:15. Actual travel and service interruptions can shift activity starts. |

Offer luggage help **before handing over the key**. Bags then remain where the guest leaves them. Use the existing carry controls, cart and reception storage; unload beside the wardrobe inside the assigned room. The reception service book includes a single batch progress entry, not a task per bag. The cart retains its four secured-parcel capacity; six bags may require two trips. An unassisted guest brings their physical luggage along the entrance/room route and places it on the room's delivery mat.

The amplifier draws **1.4** additional electrical load only during a staged rehearsal with that amplifier delivered to its owner's room. The regular circuit system sums it with room loads and heaters; it can run safely or contribute to a real overload. Loss of room power silences it. Carrying/storing/removing the delivered amplifier stops its load and sound. Room relocation revokes the old delivery until equipment reaches the new room.

Knock and ask the musician to **keep it down**, using the normal noise-warning memory, or **unplug the amplifier** for the rest of the stay. Unplugging is also available after a blackout. Relocation and compensation use the existing conversation rules. The night owl has no artificial load multiplier and no special staff-wakeup trigger.

## Implementation map

- `SpecialGuests.cs`: immutable catalogue of premium, baggage payload, activity slots and optional daily timing; small serialized frequency settings in `PrototypeSession.asset`.
- `HotelSimulation.SpecialBookings.cs`: correspondence and explicit revision-checked accept/decline. Uses `CommitBooking`, normal reservations, physical key/check-in and checkout accounting.
- `GuestScheduleSystem.Rhythm.cs`: composes a few timed slots into the existing finite daily itinerary. No special event sequence or separate guest manager.
- `GuestServiceSystem`: ordinary luggage identities extended with payload and ordinal. Stock and supplies keep their existing behaviour.
- `ServiceSupplyItem` / `SpecialLuggageAppearance`: shared physical bodies, authored suitcase/case/amplifier meshes, real collider sizes/masses, same grab/cart/storage/delivery components. Pool increased from 24 to 36.
- `ElectricalSystem` / `NoiseSystem`: active delivered amplifier is an ordinary consumer and causal room noise source. `LivingGuestAudio` plays an original procedural guitar phrase at the physical amplifier.
- LAN protocol 22 / model schema 18: special enquiries, immutable guest kind, bag payloads, switch state, distinct guest appearance and exact body-to-parcel identity survive replication and late join. Both players need 0.6.1.

No own-heater fourth guest, employees, plumbing, new quest framework, HUD banners or persistent save system are added.

## Verification scope

Six focused model cases **passed**: automatic ordinary bookings, special accept/decline and stale revisions; amplifier delivery/power/noise/quiet, carried removal and unplugging in original and restored rooms; a real breaker trip from scheduled rehearsal plus an ordinary heater; six-bag storage/delivery; the actual overnight itinerary; and JSON replica application. The single scene scenario **passed for all three guests**, using the physical reservation book/controller, real arrival route, baggage visibility, heavy-case cart attachment/movement and amplifier delivery/rehearsal in room 108. It also checks distinct LAN guest views. A separate world-serializer pass **passed**, including compression/decompression equality: 435,324 decoded bytes against the existing 524,288-byte limit, leaving room for occupied guest state.

Labelled setup adapters advance the hotel calendar, provide restoration funds, hand over a key, drop a case onto the cart and place the amplifier above its room mat; these are not a claim of a full human playthrough. Failed early scene passes exposed counter/lane baggage placement, which was corrected. Test pacing was corrected to respect the existing bounded diagnostic clock and its normal float rounding. No production clock or authority checks were bypassed.

Results: `verification/strange061/editmode-results.xml` and `playmode-results.xml`; inspected scene captures: `screenshots/strange061/`. The Windows package record and SHA-256 accompany the build. Two-computer human LAN testing remains unconfirmed. The previously deferred Alt+Tab freeze is outside this milestone.

Windows build succeeded. The complete ZIP contains 280 files (72,418,323 bytes compressed); each packaged file matches its built source by SHA-256. Archive SHA-256: `8ac1422aad655ddc170c00c28b5a62eff6dc8033e7d0274a39ef8ad88f7dc55f`.
