import json
import math
import sys

def read(path):
    rows = [json.loads(s) for s in open(path, encoding="utf-8-sig") if s.strip()]
    assert len(rows) == 4
    return {r["size"]: r for r in rows}

before, after = map(read, sys.argv[1:])
assert set(before) == set(after) == {1, 100, 1000, 10000}
for count, row in after.items():
    assert row["pages"] == before[count]["pages"] == (count + 59) // 60
    assert row["bytes"] > 1500 and not row["physical_printer"]
    assert all(math.isfinite(row[k]) and row[k] > 0 for k in ["print_ms", "save_pdf_ms", "elapsed_ms"])
    print(f"WPF {count}: {row['pages']} pages, before {before[count]['elapsed_ms']:.1f} ms, after {row['elapsed_ms']:.1f} ms")
