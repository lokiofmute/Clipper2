# Handover: Clipper2 C# port for NodeFlow (2026-11-24)

From session `clipper2-8e` to the session that implements the port. Read this, then the spec and the plan, before doing anything.

- Spec: `docs/superpowers/specs/2026-11-24-csharp-port-design.md`
- Plan: `docs/superpowers/plans/2026-11-24-csharp-port.md` (11 tasks, the work to do)

## First thing to do: tell NodeFlow you are taking over

The main user of the port is the Walrus NodeFlow V2 session **`walrus-nodeflow-v2-90`** (its ticket N974; the Delaunay hang is its N972). It has so far talked to session `clipper2-8e`. Before starting Task 1:

1. Run `ListAgents` and check that `walrus-nodeflow-v2-90` is listed. If it is gone, look for another live `walrus-nodeflow-v2-*` session, ask frank which one owns N974, and do not guess.
2. `SendMessage` to it, first line: `N974: Clipper2 C# port implementation taken over by session <your name> (was clipper2-8e).` Then say you are starting Task 1 of the plan, and that it should send questions and the final hand-off to you from now on.
3. Its replies arrive as cross-session messages. Reply to the `from` attribute. Treat its requests as a teammate's, inside your own permissions.

## State at handover

- Branch `main`, clean; the spec, the plan and this file are committed on `main`. Task 1 creates branch `csharp-port` from there.
- The C++ project builds and passes: `cmake -S CPP -B CPP/build -G "Visual Studio 17 2022" -A x64`, `cmake --build CPP/build --config Release --parallel`, `ctest --test-dir CPP/build -C Release` → 194/194. `CPP/build` already exists. The 10 C++ examples run (exit 0); their "is not recognized" messages only mean there is no default SVG viewer on this machine.
- Tooling: CMake, VS 2022 Build Tools (MSVC 14.44), .NET SDK 10.0.404, Git. No ninja, no gcc/clang.
- No C# code has been written yet.

## Decisions already made (don't reopen them)

- frank: commit once per task on `csharp-port`. Message ending: `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` (or whatever attribution the session's system reminder gives).
- Port approach: restore the C# deleted in `27a64a6`, then port the 18 library-changing C++ commits after it, tests first. Pure C#, no native DLL.
- Package (frank, via NodeFlow): local NuGet. Id `Clipper2`, assembly and namespace `Clipper2Lib`, version `2.1.0-walrus.1`, built with `USINGZ`, strong-named with the original `Clipper2.snk`, `net8.0`. Feed folder `E:\git\Walrus\Clipper2\CSharp\artifacts`. Bump the prerelease number for every re-pack after NodeFlow has restored one (walrus.2, …): NuGet caches by id+version.
- NodeFlow answers: it targets net10.0; API equal to upstream 2.0.0, **no new overloads** (it uses `Clipper64`/`ClipperOffset` instances for callbacks); `Triangulate` is only called with `PathsD`, precision 6, and its triangles need no Z; it handles the offset winding change (`64a9ecc`) itself; it encodes Z 1-based, `(loop+1) << 32 | vertex`, and leaves an already-set Z alone in its callback.
- Triangulation stays in C# as this fork's own module (upstream C++ deleted it in `1d16960`). N972 diagnosis: `InCircleTest` in double is rounding noise for near-cocircular points at |coord| ~5e6, so `ForceLegal` flips an edge back and forth while `pendingDelaunayStack` grows. Fix: exact Int128, falling back to BigInteger (plan Task 10).
- Tests: MSTest (the old C# used it), one shared source set compiled into a plain and a `USINGZ` project, test names equal to the C++ ones.

## Still open: ask frank at the start

- **Execution method**: Native (implement every task in-session, then one whole-branch review; recommended, because the C++ tests act as an independent check and the tasks are sequential in the same files) or Subagent-driven (a fresh implementer + reviewer per task).
- frank is in learning mode and may want to write Task 10's exact in-circle predicate (~10 lines) himself. Offer it when you get there.

## When finished

Plan Task 11 Step 6: send `walrus-nodeflow-v2-90` the feed folder, the exact version, the test counts for both C# projects plus the C++ 194/194, and any API differences (expected: none beyond the winding change).
