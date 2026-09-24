# MeshCaddy working notes

Follow `AGENTS.md` as the source of truth for this repository.

When changing MeshCaddy:

1. Preserve its lightweight, native Windows character.
2. Avoid adding packages unless the standard library cannot reasonably support the feature.
3. Keep model parsing separate from UI code.
4. Make folder and file operations update the visible list and current preview immediately.
5. Verify changes with `dotnet build` and report any environment limitation clearly.
