# Prototype controls

| Action | Keyboard / mouse | Gamepad |
| --- | --- | --- |
| Move | W, A, S, D | Left stick |
| Look | Mouse | Right stick |
| Run | Left Shift | Left-stick press |
| Crouch | Left Ctrl | Right-stick press |
| Interact | E | South face button (A / Cross) |
| Inventory | Tab or I | North face button (Y / Triangle) |
| Close inventory | Escape | East face button (B / Circle) |
| Flashlight | F | West face button (X / Square) |
| Inventory selection | Arrow keys, mouse click | D-pad or left stick |

Mouse look uses pointer delta; stick look is scaled by elapsed time. Opening the
inventory pauses player movement and look. The inventory is a keyboard/gamepad
selection UI with optional mouse clicking; it does not depend on legacy Input
Manager axes or an EventSystem input module. Closing it restores movement without
an extra click. The player stays crouched until the full standing capsule fits
under the ceiling. Escape outside the inventory releases
the cursor and pauses movement until a mouse click recaptures it.
