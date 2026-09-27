# Occupied room access — 0.5.4

The user reported that a closed occupied room opened on E without a knock. The door's luggage exception previously depended on any undelivered accepted suitcase assigned to the room, even when the suitcase was still at reception. That exception applied to both employees with empty hands.

The exception now requires this employee to physically carry an accepted suitcase belonging to the current room occupant, or guide a cart with that suitcase secured to it. Releasing the cart or putting down the suitcase removes that employee's delivery access. An old occupant's bag cannot unlock the new occupant's room. The door explicitly says `Agreed luggage delivery` when using the exception.

Without physical delivery, the existing knock/conversation/entry-permission path applies. A conversation request no longer overrides the guest's private activity just because a luggage job is outstanding. Prior consent for actual luggage delivery still works while the guest follows their schedule, as required by the earlier luggage specification. Inside egress, emergency hold, guest passage and service doors retain their existing behavior.

The host evaluates the physical luggage and cart driver for either actor. LAN clients receive the existing host-authored prompt and door state; there is no new snapshot field. The compatibility identifier requires both players to use 0.5.4.

Validation: source review and Windows build only. No automated test runner, new harness or live SOLO/LAN walkthrough was used for this fix. The user should verify the originally reported interaction in the new build; runtime behavior is not claimed as playtested.
