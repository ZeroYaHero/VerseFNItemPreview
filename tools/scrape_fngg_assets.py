import requests as requests
import json as json
import time as time
import re as re
from pathlib import Path

SCRIPT_DIR = Path(__file__).resolve().parent
BINDS_PATH = SCRIPT_DIR.parent / "data" / "item_image_binds.json"
CACHE_PATH = SCRIPT_DIR.parent / "data" / "fngg_assets.json"

ASSETS_URL = "https://fortnite.gg/assets"
ITEM_CATEGORY_TYPES = [
    "All-weapons",
    "Items",
    "Consumables",
    "Materials",
    "Traps"
]

USER_AGENT = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36"
DELAY = 0.25

CATEGORY_RE = re.compile(r"CH(\d)(MS\d|S[0-9X])")
CATEGORY_OVERRIDES = {
    "CH1S2": "Ch1-s2",
    "CH4MS1": None,
    "CH5S1": "Ch5-S1-underground",
    "CH5S2": "Ch5-S2-Myths-and-Mortals",
    "PreSeason": "Chapter-1-Classics",
    "Ballistic": "Ballistic",
    "Creative": None,
}

session = requests.Session()
session.headers["User-Agent"] = USER_AGENT

def get(url, params):
    time.sleep(DELAY)
    return session.get(url, params=params)

def get_season(key):
    return key.split("_", 1)[1].replace("BR_", "").replace("OG_", "").replace("_V1", "")

def get_category(season):
    if season in CATEGORY_OVERRIDES:
        return CATEGORY_OVERRIDES[season]
    match = CATEGORY_RE.fullmatch(season)
    return f"Ch{match.group(1)}-{match.group(2)}" if match else None

def get_feed_ids(url, category):
    ids = []
    page = 1
    while True:
        data = get(url, {"category": category, "infinite": "", "page": page}).json()
        ids += [asset["id"] for asset in data["assets"]]
        if not data["hasmore"]:
            break
        page += 1
    print(f"Fetched {len(ids)} assets for category {category}", flush=True)
    return ids

def save(cache):
    with open(CACHE_PATH, "w", encoding="utf-8") as f:
        json.dump(cache, f, indent=2)

with open(BINDS_PATH, encoding="utf-8") as f:
    binds = json.load(f)

try:
    with open(CACHE_PATH, encoding="utf-8") as f:
        cache = json.load(f)
except FileNotFoundError:
    cache = {"assets": {}, "seasons": {}}

for season in sorted({get_season(key) for key in binds}):
    category = get_category(season)
    if category and season not in cache["seasons"]:
        print("category", category, flush=True)
        cache["seasons"][season] = get_feed_ids(ASSETS_URL, category)
        save(cache)

ids = []
for item_category_type in ITEM_CATEGORY_TYPES:
    ids += get_feed_ids(ASSETS_URL, item_category_type)
print(len(ids), "weapon and item assets", flush=True)

for done, identifier in enumerate(ids, 1):
    if str(identifier) not in cache["assets"]:
        response = get(ASSETS_URL, {"detail": identifier})
        if response.status_code == 200:
            detail = response.json()
            cache["assets"][str(identifier)] = {
                "title": detail["title"],
                "files": [entry["file"] for entry in detail["files"]],
            }
    if done % 50 == 0:
        print(done, "of", len(ids), flush=True)
        save(cache)

save(cache)
print(len(cache["assets"]), "assets cached")
