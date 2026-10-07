#!/usr/bin/env python3
"""Names the failing tests in a Unity play-mode results file.

The workflow that runs the play-mode tests has one output channel that can be read from the
environment that drives it: workflow annotations. Logs and artifacts both live on storage that
environment cannot reach, and the results file is one very long line of XML, so the step that
reported "1 test(s) failed" could not say which one — the name is an attribute on a
<test-case> element somewhere in that line, and grep cannot pick it out.

This prints one block per failing case: the name, and the message the framework recorded, with
the whitespace flattened so an annotation can carry it. Its exit code is the number of failures
capped at 1, so a caller can use it as a test result as well as a report.

Usage:  python3 tools/ci/failing_tests.py <results.xml>
"""

from __future__ import annotations

import os
import re
import sys


def failing(text: str) -> "list[tuple[str, str]]":
    """Every failing test case: its name, and the message recorded for it."""
    out = []

    for match in re.finditer(r"<test-case\s[^>]*>", text):
        tag = match.group(0)
        if 'result="Failed"' not in tag:
            continue

        name = re.search(r'name="([^"]*)"', tag)
        case = text[match.end():]
        end = case.find("</test-case>")
        if end >= 0:
            case = case[:end]

        message = ""
        found = re.search(r"<message>(.*?)</message>", case, re.S)
        if found:
            message = found.group(1)
            message = re.sub(r"<!\[CDATA\[|\]\]>", "", message)
            message = re.sub(r"\s+", " ", message).strip()

        out.append((name.group(1) if name else "?", message))

    return out


def main(argv: "list[str]") -> int:
    if len(argv) != 2:
        print("usage: failing_tests.py <results.xml>", file=sys.stderr)
        return 2

    path = argv[1]
    if not os.path.exists(path):
        print("FAILED: no results file at %s" % path, file=sys.stderr)
        return 2

    with open(path, "r", encoding="utf-8", errors="replace") as handle:
        text = handle.read()

    cases = failing(text)
    for name, message in cases:
        print("FAILED: %s" % name)
        if message:
            print("  WHY: %s" % message[:900])

    if not cases:
        # A run whose summary says something failed but whose cases all passed is the framework
        # reporting a suite-level error, which is also worth reading out loud.
        summary = re.search(r"result=\"Failed[^\"]*\"[^>]*total=\"(\d+)\"[^>]*failed=\"(\d+)\"", text)
        if summary:
            print("FAILED: no failing case, but the run reports %s of %s failed"
                  % (summary.group(2), summary.group(1)))

    return 1 if cases else 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
