# Building Modeling Tool

## Overview

The Building Modeling Tool is a Unity editor window for quickly creating and editing hollow buildings with a flat roof.

You can:

- Create a cube-like building or a multi-sided building shell.
- Drag building corner anchors in scene view.
- Split any wall into two walls (except walls that currently contain a window).
- Set wall thickness.
- Generate both a ceiling and a floor for polygon footprints.
- Click walls to create windows or doors.
- Drag window and door corners to resize openings.
- Assign a different material to each wall.
- Toggle trim, window sill, and glass display for openings.
- Snap draggable anchors to a configurable grid.

## Open The Tool

1. Open Unity.
2. In the top menu, choose either:
   - `Lilithe > Tools > Building Modeling Tool`
   - `CozyCon > Tools > Building Modeling Tool`

## Create A New Building

1. In the tool window, click **New Building**.
2. Set:
   - **Regular Side Count** (4 gives a cube-style footprint)
   - **Regular Radius**
   - **Wall Height**
   - **Wall Thickness**
   - **Ceiling Thickness**
   - **Floor Thickness**
   - **Snap To Grid** and **Grid Size** (optional)
3. Click **Reset Footprint To Regular Shape** to regenerate the footprint from the current side count and radius.

When no walls exist yet in Edit Building mode, you can also click a flat horizontal collider surface (or the floor plane) in scene view to spawn a new hollow cube building at the click point.

## Edit Building Mode

1. Click **Edit Building** in the tool window.
2. In scene view, drag the corner anchor spheres.
3. The debug axis cross and sphere mark each draggable anchor.

## Split Wall

1. In **Edit Building** mode, click **Split Wall At Click Point**.
2. In scene view, click directly on the wall where you want the split corner inserted.
3. The wall is split at that exact clicked point, so the footprint gains one extra corner.
4. Ceiling and floor update from the new footprint, so they are split at the same wall location.
5. You cannot split a wall if a **window** is defined on that wall.

This is useful for creating non-rectilinear and custom n-gon building shapes.

## Window Creation Mode

1. Click **Window Creation**.
2. Click a wall from either outside or inside.
3. A window is created centered at the clicked wall hit.
4. Window defaults are controlled by:
   - **Window Trim**
   - **Window Sill**
   - **Window Glass**

## Door Creation Mode

1. Click **Door Creation**.
2. Click a wall from either outside or inside.
3. A standard door is created and centered on the wall hit.
4. If the wall is too small, the door is automatically reduced to fit the wall.
5. Door trim visibility uses **Door Trim**.

## Edit Window And Door Corners

1. Return to **Edit Building** mode.
2. Drag the corner anchors around each opening to resize it.
3. Opening settings can also be edited in the **Openings** list:
   - Size
   - Trim on/off
   - Sill on/off (windows)
   - Glass on/off (windows)

## Material Assignment

Use the tool fields to set:

- **Default Wall Material**
- **Ceiling Material**
- **Floor Material**
- **Trim Material**
- **Glass Material**
- **Per-Wall Materials** (`Wall 1`, `Wall 2`, etc.)

Each wall can have a separate material.

## Notes

- Walls, ceiling, and floor are generated under the building draft object.
- Ceiling and floor are generated as polygon prism meshes that follow the current n-gon footprint.
- Concave (non-rectilinear) polygon footprints are triangulated so ceiling and floor continue to build correctly.
- Openings are represented in geometry and can also spawn visual trim/sill/glass objects.
- Use **Rebuild Building** if you want to force a fresh geometry update.
- Use **Delete All Openings** to clear every door/window quickly.
