import json as json
import re as re
import difflib as difflib
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
BINDS_PATH = SCRIPT_DIR.parent / "data" / "item_image_binds.json"
CACHE_PATH = SCRIPT_DIR.parent / "data" / "fngg_assets.json"

CDN_URL = "https://fnggcdn.com/assets/"
CUTOFF = 0.85

VARIANT_RE = re.compile(r"\([^)]*\)")
PLAIN_RE = re.compile(r"[^a-z0-9]")

def normalize(text):
    return PLAIN_RE.sub("", VARIANT_RE.sub("", text).lower())

def get_season(key):
    return key.split("_", 1)[1].replace("BR_", "").replace("OG_", "").replace("_V1", "")

with open(BINDS_PATH, encoding="utf-8") as f:
    binds = json.load(f)
with open(CACHE_PATH, encoding="utf-8") as f:
    cache = json.load(f)

binds['base'] = CDN_URL

index = {}
for identifier, asset in cache["assets"].items():
    if asset["files"]:
        index.setdefault(normalize(asset["title"]), []).append(identifier)

matched = []
missed = []
for key, url in binds['items'].items():
    if url:
        continue
    name = normalize(key.split("_")[0])
    ids = index.get(name)
    if not ids:
        close = difflib.get_close_matches(name, index, n=1, cutoff=CUTOFF)
        ids = index[close[0]] if close else []
    if not ids:
        missed.append(key)
        continue
    season = {str(identifier) for identifier in cache["seasons"].get(get_season(key), [])}
    ids = [identifier for identifier in ids if identifier in season] or ids
    binds['items'][key] = cache["assets"][ids[0]]["files"][0]
    matched.append((key, cache["assets"][ids[0]]["title"]))

with open(BINDS_PATH, "w", encoding="utf-8") as f:
    json.dump(binds, f, indent=2)

print(len(matched), "matched,", len(missed), "unmatched")
