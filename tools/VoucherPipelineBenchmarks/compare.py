import json
import math
import sys

def read(path):
    rows = [json.loads(s) for s in open(path, encoding="utf-8-sig") if s.strip()]
    assert len(rows) == 12, (path, len(rows))
    return {(r["size"], r["service"]): r for r in rows}

before, after = map(read, sys.argv[1:3])
assert before.keys() == after.keys()
for key, new in after.items():
    old = before[key]
    assert old["confirmed"] == new["confirmed"] == new["size"], key
    assert new["errors"] == new["retries"] == 0, key
    assert new["readbacks"] == new["size"], (key, "every card requires exact-ID verification")
    # Three extra cross-service reads before local adoption. The UM loop
    # removes per-card identity requests; HS/PPP spend that budget on proof.
    assert new["requests"] <= old["requests"] + 3, (key, old["requests"], new["requests"])
    if new["service"] == "userman" and new["size"] >= 100:
        assert new["requests"] <= old["requests"] - new["size"] + 3, key
    for stage, value in new["stages_ms"].items():
        assert value is None and stage == "Print" or isinstance(value, (float, int)) and math.isfinite(value) and value >= 0, (key, stage)
    print(f"{key}: API {old['requests']} -> {new['requests']}; confirmed {new['confirmed']}; errors/retries 0")
print("All 12 pipeline comparisons passed; wall-clock timings are descriptive, not flaky pass/fail thresholds.")
