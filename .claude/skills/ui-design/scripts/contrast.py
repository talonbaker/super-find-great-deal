#!/usr/bin/env python3
"""
WCAG 2.x contrast checker for UI palettes.

Contrast is a property of *pairs*, not of palettes — a "compliant palette" does not
exist. This checks the pairs you actually ship, and suggests fixes for the ones that fail.

Usage
-----
  # one pair
  contrast.py "#e8e8ec" "#121214"

  # a whole text ramp against one or more backgrounds
  contrast.py --audit "#e8e8ec,#a1a1aa,#71717a,#4c6ef5" --on "#121214,#1c1c20"

  # nearest passing variant of a foreground against a background
  contrast.py --fix "#71717a" --on "#121214" --target 4.5

  # named pairs (label:hex) for readable output
  contrast.py --audit "text-primary:#e8e8ec,text-muted:#71717a" --on "surface:#121214"

Options
-------
  --level {AA,AAA}   conformance level for the summary verdict (default AA)
  --large            treat text as large (>=24px, or >=18.66px bold): 3:1 AA / 4.5:1 AAA
  --ui               treat as a UI component / focus ring / meaningful graphic: 3:1
  --target RATIO     explicit ratio for --fix (overrides level/size flags)
  --json             machine-readable output

Exit status is 1 if any checked pair fails, so this can gate CI.
"""

from __future__ import annotations

import argparse
import colorsys
import json
import re
import sys

# ---------------------------------------------------------------- colour maths


def parse_color(value: str) -> tuple[float, float, float]:
    """Accept #rgb, #rrggbb, rrggbb, or 'r,g,b' with 0-255 components."""
    s = value.strip().lstrip("#")
    if re.fullmatch(r"[0-9a-fA-F]{3}", s):
        s = "".join(c * 2 for c in s)
    if re.fullmatch(r"[0-9a-fA-F]{6}", s):
        return tuple(int(s[i : i + 2], 16) / 255 for i in (0, 2, 4))  # type: ignore[return-value]
    parts = [p for p in re.split(r"[,\s]+", value.strip()) if p]
    if len(parts) == 3:
        try:
            return tuple(max(0.0, min(255.0, float(p))) / 255 for p in parts)  # type: ignore[return-value]
        except ValueError:
            pass
    raise ValueError(f"cannot parse colour: {value!r}")


def to_hex(rgb: tuple[float, float, float]) -> str:
    return "#" + "".join(f"{round(max(0.0, min(1.0, c)) * 255):02x}" for c in rgb)


def _linearize(c: float) -> float:
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def relative_luminance(rgb: tuple[float, float, float]) -> float:
    r, g, b = (_linearize(c) for c in rgb)
    return 0.2126 * r + 0.7152 * g + 0.0722 * b


def contrast_ratio(fg: tuple[float, float, float], bg: tuple[float, float, float]) -> float:
    lf, lb = relative_luminance(fg), relative_luminance(bg)
    hi, lo = max(lf, lb), min(lf, lb)
    return (hi + 0.05) / (lo + 0.05)


def composite(fg: tuple[float, float, float], alpha: float,
              bg: tuple[float, float, float]) -> tuple[float, float, float]:
    """Flatten a translucent foreground onto an opaque background."""
    return tuple(fg[i] * alpha + bg[i] * (1 - alpha) for i in range(3))  # type: ignore[return-value]


# ---------------------------------------------------------------- thresholds

def required_ratio(level: str, large: bool, ui: bool) -> float:
    if ui:
        return 3.0                      # WCAG 1.4.11 Non-text Contrast (AA)
    if level == "AAA":
        return 4.5 if large else 7.0    # WCAG 1.4.6
    return 3.0 if large else 4.5        # WCAG 1.4.3


def grade(ratio: float) -> str:
    """Everything this pair is good for, regardless of the requested level."""
    ok = []
    if ratio >= 7.0:
        ok.append("AAA text")
    elif ratio >= 4.5:
        ok.append("AA text")
    if ratio >= 4.5:
        ok.append("AAA large")
    elif ratio >= 3.0:
        ok.append("AA large")
    if ratio >= 3.0:
        ok.append("UI")
    return ", ".join(ok) if ok else "fails everything"


# ---------------------------------------------------------------- fixing

def suggest(fg: tuple[float, float, float], bg: tuple[float, float, float],
            target: float) -> tuple[tuple[float, float, float], float] | None:
    """
    Nearest passing colour, found by walking HLS lightness away from the background.
    Hue and saturation are preserved, so the result stays on-brand.
    """
    if contrast_ratio(fg, bg) >= target:
        return None
    h, l, s = colorsys.rgb_to_hls(*fg)
    # Move away from the background's luminance: lighter on dark, darker on light.
    direction = 1.0 if relative_luminance(bg) < 0.18 else -1.0
    best = None
    steps = 200
    for i in range(1, steps + 1):
        candidate_l = l + direction * (i / steps) * (1.0 if direction > 0 else 1.0)
        if not 0.0 <= candidate_l <= 1.0:
            break
        cand = colorsys.hls_to_rgb(h, candidate_l, s)
        if contrast_ratio(cand, bg) >= target:
            best = cand
            break
    if best is None:
        # Fall back to the extreme in that direction, then try the other way.
        for direction in (1.0, -1.0):
            cand = colorsys.hls_to_rgb(h, 1.0 if direction > 0 else 0.0, s)
            if contrast_ratio(cand, bg) >= target:
                best = cand
                break
    if best is None:
        return None
    return best, contrast_ratio(best, bg)


# ---------------------------------------------------------------- input parsing

def parse_list(spec: str) -> list[tuple[str, tuple[float, float, float]]]:
    """
    Parse 'text-primary:#fff,#000' into [(label, rgb), ...]; labels are optional and
    default to the hex itself. Comma-separated, so 'r,g,b' triples are not accepted
    here — use hex in list form.
    """
    out = []
    for item in spec.split(","):
        item = item.strip()
        if not item:
            continue
        if ":" in item:
            label, _, value = item.partition(":")
            label, value = label.strip(), value.strip()
            label = label or value
        else:
            label = value = item
        out.append((label, parse_color(value)))
    return out


# ---------------------------------------------------------------- reporting

GREEN, RED, YELLOW, DIM, RESET = "\033[32m", "\033[31m", "\033[33m", "\033[2m", "\033[0m"


def colorize(text: str, code: str, enabled: bool) -> str:
    return f"{code}{text}{RESET}" if enabled else text


def main() -> int:
    p = argparse.ArgumentParser(
        description="WCAG 2.x contrast checker.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__.split("Usage\n-----")[1] if "Usage\n-----" in __doc__ else None,
    )
    p.add_argument("pair", nargs="*", help="foreground and background colour")
    p.add_argument("--audit", help="comma-separated foregrounds (optionally label:hex)")
    p.add_argument("--on", help="comma-separated backgrounds (optionally label:hex)")
    p.add_argument("--fix", help="foreground to find a passing variant of")
    p.add_argument("--alpha", type=float, default=1.0,
                   help="foreground alpha 0-1, composited onto the background first")
    p.add_argument("--level", choices=["AA", "AAA"], default="AA")
    p.add_argument("--large", action="store_true", help="large text (>=24px, or >=18.66px bold)")
    p.add_argument("--ui", action="store_true", help="UI component / focus ring / meaningful graphic")
    p.add_argument("--target", type=float, help="explicit target ratio")
    p.add_argument("--json", action="store_true", help="machine-readable output")
    args = p.parse_args()

    use_color = sys.stdout.isatty() and not args.json
    target = args.target or required_ratio(args.level, args.large, args.ui)

    try:
        if args.fix:
            if not args.on:
                p.error("--fix requires --on")
            fg = parse_color(args.fix)
            bg = parse_list(args.on)[0][1]
            if args.alpha < 1.0:
                fg = composite(fg, args.alpha, bg)
            current = contrast_ratio(fg, bg)
            result = suggest(fg, bg, target)
            if args.json:
                print(json.dumps({
                    "foreground": to_hex(fg), "background": to_hex(bg),
                    "ratio": round(current, 2), "target": target,
                    "passes": current >= target,
                    "suggestion": to_hex(result[0]) if result else None,
                    "suggestion_ratio": round(result[1], 2) if result else None,
                }, indent=2))
            elif result is None:
                print(f"{to_hex(fg)} on {to_hex(bg)} = {current:.2f}:1 — "
                      + ("already passes." if current >= target
                         else f"no variant of this hue reaches {target}:1; change the background."))
            else:
                fixed, ratio = result
                print(f"{to_hex(fg)} on {to_hex(bg)} = {current:.2f}:1  (needs {target}:1)")
                print(f"  → {to_hex(fixed)} gives {ratio:.2f}:1  (same hue and saturation)")
            return 0 if (current >= target or result) else 1

        if args.audit:
            if not args.on:
                p.error("--audit requires --on")
            foregrounds = parse_list(args.audit)
            backgrounds = parse_list(args.on)
            rows, failures = [], 0
            width = max((len(l) for l, _ in foregrounds), default=10)
            for bg_label, bg in backgrounds:
                if not args.json:
                    print(f"\n  on {bg_label} {to_hex(bg)}")
                    print(f"  {'':{width}}  {'ratio':>9}  verdict")
                    print(f"  {'-' * width}  {'-' * 9}  {'-' * 30}")
                for fg_label, fg_raw in foregrounds:
                    fg = composite(fg_raw, args.alpha, bg) if args.alpha < 1.0 else fg_raw
                    ratio = contrast_ratio(fg, bg)
                    passes = ratio >= target
                    failures += 0 if passes else 1
                    rows.append({
                        "foreground": fg_label, "foreground_hex": to_hex(fg),
                        "background": bg_label, "background_hex": to_hex(bg),
                        "ratio": round(ratio, 2), "passes": passes, "grade": grade(ratio),
                    })
                    if not args.json:
                        mark = "PASS" if passes else "FAIL"
                        code = GREEN if passes else RED
                        print(f"  {fg_label:{width}}  {ratio:6.2f}:1  "
                              f"{colorize(mark, code, use_color)}  "
                              f"{colorize(grade(ratio), DIM, use_color)}")
            if args.json:
                print(json.dumps({"target": target, "results": rows,
                                  "failures": failures}, indent=2))
            else:
                print(f"\n  target {target}:1 — "
                      + colorize(f"{failures} failing pair(s)", RED if failures else GREEN, use_color)
                      + f" of {len(rows)}\n")
            return 1 if failures else 0

        if len(args.pair) == 2:
            fg_raw, bg = parse_color(args.pair[0]), parse_color(args.pair[1])
            fg = composite(fg_raw, args.alpha, bg) if args.alpha < 1.0 else fg_raw
            ratio = contrast_ratio(fg, bg)
            passes = ratio >= target
            if args.json:
                print(json.dumps({
                    "foreground": to_hex(fg), "background": to_hex(bg),
                    "ratio": round(ratio, 2), "target": target,
                    "passes": passes, "grade": grade(ratio),
                }, indent=2))
            else:
                print(f"\n  {to_hex(fg)} on {to_hex(bg)}")
                print(f"  {ratio:.2f}:1   "
                      + colorize("PASS" if passes else "FAIL", GREEN if passes else RED, use_color)
                      + f" against {target}:1")
                print(f"  usable for: {grade(ratio)}")
                if not passes:
                    s = suggest(fg, bg, target)
                    if s:
                        print(f"  try {to_hex(s[0])} → {s[1]:.2f}:1")
                print()
            return 0 if passes else 1

        p.print_help()
        return 2

    except ValueError as exc:
        print(f"error: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
