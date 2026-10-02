# AgentCapture process protocol

Read command syntax and defaults from the installed EXE via the adapter's `help` command. The adapter forwards CLI arguments directly, launches without a console window, and preserves the tool's stdout JSON and exit code.

Successful capture fields: `schemaVersion`, `ok`, `requestedMethod`, `method`, `output`, `target`, `image`, `foreground`, `warnings`, `attempts`, `elapsedMs`.

- `target`: hexadecimal `hwnd`, `pid`, title, process/class names, window bounds, visible/minimized/cloaked/foreground state, and display affinity when queryable.
- `image`: actual width/height, PNG SHA-256, straight alpha, `uniformRgb`, `contentVerified:false`, and freshness warnings.
- `foreground`: hook availability, before/after handles, observed changes, and `targetActivationRequested:false`.
- A failure has `ok:false` and `error.code/message/exitCode`; capture attempts also carry per-backend diagnostics. Preflight failures can omit capture metadata.

Exit codes: 0 success; 2 arguments, ambiguous target or existing output; 3 capture/state failure; 4 timeout; 5 unexpected runtime failure. The adapter uses the same error envelope for tool discovery, launch and timeout errors.

Handle `ambiguous_window` by selecting an exact HWND. Refresh discovery for `invalid_window` or `window_changed`. `window_resized` and `window_state_changed` can justify one retry after the target settles. Preserve an existing output for `output_exists`. Report a persistent state restriction, blank frame or timeout; choose another backend only within the user's capture request.

Output is published from a temporary PNG only after a successful worker response. A terminated supervisor can leave a `.agentcapture-*.png` temporary file. `auto` never falls back to foregrounding the target or copying the live screen.

The native tool limits frames to 40 megapixels, preserves system capture restrictions, and remains at caller privilege. WGC rejects hidden, cloaked or minimized targets. PrintWindow returns whatever the target/window system draws, which may not represent full application content.
