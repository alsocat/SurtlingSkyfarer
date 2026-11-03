# Changelog

## 0.1.8

- Separate front/back wing surfaces and use a cloth item material to address flashing and whitening.
- Synchronize cargo during physics as well as rendering to avoid lag at low frame rates.
- Stop hull/cargo distant streaming and suspend aircraft physics until terrain loads, protecting parked aircraft across area reloads.
- Add per-plane cargo upgrades: 10 → 20 → 30 slots, costing 20 Fine Wood and 10 Copper each. Expanded contents persist through streaming and saves.
- Bind one Wisp per plane for a native 25-metre mist-clearing effect that follows the aircraft.
- Add V to toggle vertical takeoff/hover. `VtolEnabled = true` by default; server admins can disable it in the adjacent config.
- Enable R reverse and braking while floating on water.
- Add Shift + E at the unoccupied helm for upgrades and unstuck. Unstuck checks occupants, speed, wards, cooldown and destination clearance before lifting/levelling the plane.
- Keep existing recipes, speed, durability defaults, aircraft and cargo compatible. Update all clients and the server together.

## 0.1.7

- Add `DurabilityEnabled` (default false) and `MaxHealth` (default 1800) to the adjacent config file.
- Enable normal health damage, destruction, and hammer repairs when durability is on, including crash damage.
- Preserve each aircraft's health percentage when maximum health changes, including saved aircraft from earlier versions.
- Keep structural and roof-exposure wear disabled for flight. Disabling durability does not automatically repair damage.
- Synchronize durability and maximum health through Jötunn's server settings.
- Add missing durability entries to old configs without replacing custom recipe/speed values or comments.

## 0.1.6

- Fix hammer dismantling: explicitly enable removal instead of inheriting the Karve prohibition. Normal workbench, ward, and occupancy checks remain.
- Add a commented `Skyfarer.cfg` beside the DLL. Edit it before starting the game/server to change recipe materials, quantities, and powered top speed.
- Synchronize server settings to multiplayer clients through Jötunn.
- Validate recipe entries and bound speed to 10–100 m/s; defaults remain unchanged.

## 0.1.5

- Pilot boarding transfers aircraft simulation ownership to the pilot.
- Prevent vanilla boat logic from handing aircraft ownership to passengers.
- Tolerate lagged seated-player positions and preserve authenticated heartbeat
  expiry for disconnected riders.
- Preserve valid boarding replies across ownership handoffs.
- Cut throttle when everyone leaves; riders standing on deck keep it running.
- Add a ten-slot cargo chest to new and existing aircraft, with persistent
  contents and drops on deliberate dismantling.
- Keep cargo ownership separate from aircraft ownership and preserve their
  link after world loading changes object IDs.
- Replace the recipe's Iron requirement with 25 Copper.
- Preserve the actual DLL build time in release ZIPs.

## 0.1.4

- Allow continuous pitch loops and barrel rolls.
- Map A/D to roll and Q/E to rudder.
- Make Space the only manual pilot exit.
- Add R for ground reverse and right mouse for wheel brakes.

## 0.1.3

- Add persistent throttle, adjusted with Shift/Ctrl.
- Map W to nose down and S to nose up.

## 0.1.2

- Add four wooden wagon wheels with suspension, steering, and braking.

## 0.1.1

- Make the aircraft indestructible while retaining collisions and deliberate
  hammer dismantling.

## 0.1.0

- Initial wooden aircraft with one pilot seat and four passenger seats.
