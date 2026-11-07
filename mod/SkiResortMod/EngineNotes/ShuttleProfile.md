# ShuttleProfile: the jig-back tram's speed profile, cycle, clock and length quantum

All math is double. `ShuttleProfile` is stateless: the cabin position is a pure function of the clock.

## The profile

Cruise speed v, symmetric acceleration / deceleration a, path L (dock to dock), dwell D at each terminal. The ramps together cover
d_ad = v^2/a.

- L >= d_ad (trapezoid): trip T = L/v + v/a. s(t) = 1/2 a t^2 up to v/a, v^2/(2a) + v (t - v/a) up to T - v/a, then
  L - 1/2 a (T - t)^2.
- L < d_ad (triangle): T = 2 sqrt(L/a), peak sqrt(aL) < v. s(t) = 1/2 a t^2 up to T/2, then L - 1/2 a (T - t)^2.

The profile is continuous in s and speed, and it is monotone. For the tram (v 10, a 0.8, D 60, `SkiCatalog`'s tram row): 1 km gives a trip of 112.5 s and a cycle of
345 s. 100 m is a triangle, 22.36 s. The boundary is 125 m.

## The cycle

P = 2(T + D). tau = t - P x floor(t / P) (clamped to [0, P)).

- Cabin A:
  - tau < D gives u 0 (DwellBase).
  - tau < D + T gives u = s(tau - D)/L (Up).
  - tau < 2D + T gives 1 (DwellTop).
  - Otherwise, 1 - s(tau - 2D - T)/L (Down).
- Cabin B is 1 - u (both dock at the same instant: one haul rope).
- HOLD (no power): u = tau < D + T ? 0 : 1, the terminal of cabin A's latest dwell.
- Invalid input (L, v, a <= 0 or not finite, D < 0 or not finite, t not finite) gives u 0, DwellBase.

## The clock

`TimeSOf` is ChairPin's clock bit for bit (`ChairPinSystem.cs:1132-1135`: (frameIndex + frameTime) / 60 in double, RenderingSystem's
frame counter). It is frozen on pause, smooth at any speed, and resnapped to the saved frame on load. 60 frames = 1 s of movement at 1x. One
game day = 262,144 frames = 4,369 s (movement runs about 20x slower than the clock), so a 1 km tram does about 12.7 round trips a
game day.

## Length quantum

tau depends on P, so a rebuild that moves L by delta shifts a cabin by about (t / P) x 2 delta / v. At t 1e6 s, 1 mm moves it about
5.8 m. `QuantizeLengthM` rounds a path length to 1 cm before it reaches the profile, so float-noise rebuilds (identical topology,
last-bit differences) give the same P.
