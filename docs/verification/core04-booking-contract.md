# Phase 2 booking integration contract (implementation pending phase 1 gate)

All times below are absolute values on `HotelGameClock`, formatted through `HotelCalendar`. The calendar never resets at booking, check-in, checkout, midnight or reporting.

Root model API to implement:

- `ScheduledBookingOffer`: `Id`, `Application` (`BookingApplication`), `ArrivalDay`, `ArrivalAt`, `SleepAt`, `CheckoutAt`. One-night checkout is on arrival day + 1. Reference prices are independent of calendar day; no Day 3 escalation.
- `HotelReservation`: stable `Id` matching its offer/guest identity, `Offer`, `RoomId`, `Price`, `ActorId`, `Revision`, and status (`Reserved`, `Arrived`, `Completed`, `Cancelled`). Terminal retention is bounded. A future reservation is not a room occupant.
- `HotelSimulation.BookingOffers` and `Reservations`: immutable views. Offers cover today and tomorrow; refresh through ordinary ticking. Capacity forecasts are added later and use measured demand, not room counts as a failure trigger.
- `AcceptBooking(int actorId, string offerId, int roomId, int price)`, `CancelBooking(int actorId, string reservationId)`, and `SetBookingPrice(int actorId, string reservationId, int price)`: validate before mutation, emit an event only on success. Edits/cancellation stop at arrival. Prices use the existing valid price grid. Booking a future dirty room is allowed and carries a readiness warning.
- `CanReserveRoom(int roomId, ScheduledBookingOffer offer)` checks interval overlap, including extensions on a currently active guest. It cannot overwrite a current occupant/reservation. A room can hold separate bookings on successive nights.
- Guest stays are materialized when arrival is due, not for every future application. `Schedules.AttachStay(guest, arrivalDay, arrivalAt, sleepAt, checkoutAt)` gives the existing FSM a schedule. Physical presentation still drives reception/room/exit arrivals.
- The current `RoomState.ReservedGuestId` is assigned only when the room is free of an earlier occupant/current reservation. Dirt/turnover can still block physical key handoff. An arriving guest waiting for the previous occupant must not acquire room ownership early.
- Checkout payment is posted once per stay. No-show/no-room-time receipts charge zero under the existing satisfaction rules. The interval report displays these receipts but does not post their revenue again. Guest body exit and departure tokens continue independently of payment.

Agency Life owns the absolute schedule method, non-reset daily service budget/stock behavior, stable guest presentation slots, and guest-life tests. Root owns offers/reservations/materialization, payment, bounded retention and snapshot model integration. Agency Interface owns continuous GameSession command routing, minimal working booking UI, production activation, fixture opt-out and LAN command/wire integration. Operations UI polish remains phase 9.

Phase 2 must include enough wire state for its new booking operations to work in LAN; phase 3 then verifies and hardens all boundary persistence. No mixed-version compatibility is claimed. Historical model and scene tests explicitly use legacy mode; continuous acceptance tests exercise the production configuration.
