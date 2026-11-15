"""Validate deterministic API/payload/row budgets; elapsed CPU time is descriptive."""
import json
import sys
from pathlib import Path


def load(path):
    rows = [json.loads(line) for line in Path(path).read_text(encoding="utf-8-sig").splitlines() if line.strip()]
    result = {(row["scenario"], row["rows"]): row for row in rows}
    assert len(rows) == len(result) == 16, "Missing or duplicate benchmark cases"
    return result


before, after = map(load, sys.argv[1:3])
assert before.keys() == after.keys(), "Different workloads"
for key, old in before.items():
    new = after[key]
    assert new["returned_rows"] == old["returned_rows"], (key, "Lost records")
    assert new["errors"] == old["errors"] == new["retries"] == old["retries"] == 0, key
    assert new["requests"] <= old["requests"], (key, "More API requests")
    assert new["response_bytes"] <= old["response_bytes"], (key, "More response data")
    print(f"{key[0]} n={key[1]}: requests {old['requests']} -> {new['requests']}; "
          f"bytes {old['response_bytes']} -> {new['response_bytes']}; rows={new['returned_rows']}")
