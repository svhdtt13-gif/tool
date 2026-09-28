# XuyenChi AutoTools — UI Animation Pack

Public handoff for the XuyenChi AutoTools animated UI theme.

## Design target
- Large red route: blue whale carrying black cat, looping around the entire UI.
- Smaller purple route: blue whale swimming through the UI.
- Yellow area: black cat idle/run-in-place, then lick/stretch interactions.
- Animated water ribbon around the window and animated water in the title bar.
- Clouds, bubbles, stars and soft atmospheric particles inside/outside the UI.
- Button hover: whale wraps around the button.
- Button click: water splash + bubbles + glow.
- Cat interaction: lick + stretch, then return to its route.
- All animation loops smoothly and must not block UI input.

## Integration rules
1. Treat animation as an independent overlay layer above the existing UI.
2. Do not change or migrate the existing database.
3. Do not change business logic, client management, scheduler, API or existing V1 behavior unless strictly required by the animation adapter.
4. Prefer transform/opacity/filter, SVG motion paths, WAAPI/requestAnimationFrame; avoid continuous layout animation.
5. Pause animation while minimized/backgrounded and support `prefers-reduced-motion`.
6. Target 60 FPS where practical; keep particle counts bounded.
7. On resize, all routes and effects must scale with the window.
8. Test real buttons, tabs, client list, scheduler, database access and long-running sessions before packaging.

## Prototype
Open `demo.html` for a self-contained motion prototype showing the two routes, water border, title wave, cloud motion and button interactions.

## Final asset contract
Replace prototype SVG/vector placeholders with final transparent 3D whale/cat assets without changing the motion/state controller. Keep separate text and no-text button assets.

## Definition of done
The real app must have the animated UI—not a static screenshot/video—with route motion, hover/click interactions, cat states, water effects, background atmosphere, resize support and no regression to V1.
