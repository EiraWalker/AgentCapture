"""Example Agent process adapter; Python standard library only."""
import argparse
import json
from pathlib import Path
import subprocess
import sys

TOOL = Path(__file__).resolve().parents[1] / "bin" / "win-x64" / "AgentCapture.exe"


def invoke(*arguments: str) -> tuple[int, dict]:
    process = subprocess.run(
        [str(TOOL), *arguments],
        capture_output=True,
        text=True,
        encoding="utf-8",
        timeout=20,  # CLI's default capture budget is 8 seconds; allow process startup and cleanup.
        creationflags=subprocess.CREATE_NO_WINDOW,
    )
    try:
        result = json.loads(process.stdout)
    except json.JSONDecodeError as error:
        raise RuntimeError(f"Tool did not return JSON: {process.stderr.strip()}") from error
    return process.returncode, result


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--hwnd", required=True, help="Window handle returned by AgentCapture list")
    parser.add_argument("--output", required=True)
    parser.add_argument("--method", choices=("wgc", "printwindow", "auto"), default="wgc")
    args = parser.parse_args()
    code, response = invoke("capture", "--hwnd", args.hwnd, "--method", args.method, "--output", args.output)
    print(json.dumps(response, ensure_ascii=False))
    sys.exit(code)
