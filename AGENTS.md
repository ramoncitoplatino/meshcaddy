# MeshCaddy contributor guide

## Product goal

MeshCaddy is a lightweight Windows desktop browser for 3D model files. It is designed for quickly browsing a folder of models, previewing each model, and performing routine file and folder operations without launching a slicer or CAD program.

## Technical direction

- Build a native WPF application targeting .NET 8 for Windows.
- Keep runtime dependencies to a minimum. Prefer .NET and WPF APIs over third-party packages.
- Parse binary and ASCII STL files directly.
- Read 3MF model XML from the ZIP container with `System.IO.Compression` and `System.Xml.Linq`.
- Render meshes with WPF `Viewport3D`.
- Keep file operations explicit, recoverable where practical, and reflected immediately in the browser list.

## UX rules

- Opening a folder should immediately select and preview its first supported model.
- Previous/next controls and keyboard shortcuts should support rapid review.
- Selection, loading, parsing, and file-operation errors must be shown without crashing the app.
- Expensive parsing must run off the UI thread.
- The interface must remain usable on common Windows display scaling settings.

## Validation

- Run `dotnet build` after code changes.
- Add focused tests for parsers and non-UI logic when the behavior is complex enough to regress.
- Do not commit build output from `bin/` or `obj/`.
