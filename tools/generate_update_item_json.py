import requests as requests
import re as re
import json as json
from pathlib import Path

# Script that generates/updates json for items and their thumbnail urls

SCRIPT_DIR = Path(__file__).resolve().parent
JSON_PATH = SCRIPT_DIR.parent / "data" / "item_image_binds.json"

CLASS_RE = re.compile(r"(\w+)(?:<\w+>)*\s*:=\s*class(?:<\w+>)*\s*\(\s*entity\s*\)")
RARITY_RE = re.compile(r"_(Common|Uncommon|Rare|Epic|Legendary|Mythic|Exotic)")

def get_module_body(text: str, module_name: str) -> str:
    header = re.search(rf"\b{re.escape(module_name)}(?:<\w+>)*\s*:=\s*module\s*\{{", text)
    if not header:
        raise ValueError(f"Module {module_name} not found")

    start = header.end()
    depth = 1
    i = start
    while i < len(text) and depth > 0:
        if text[i] == "{":
            depth += 1
        elif text[i] == "}":
            depth -= 1
        i += 1
    return text[start:i - 1]

fortnite_verse_digest = requests.get("https://raw.githubusercontent.com/vz-creates/uefn/refs/heads/main/Modules/FortniteGame/Fortnite/Fortnite.digest.verse")

items_module_body = get_module_body(fortnite_verse_digest.text, "Items")
weapons_module_body = get_module_body(fortnite_verse_digest.text, "Weapons")

item_names = CLASS_RE.findall(items_module_body)
weapon_names = CLASS_RE.findall(weapons_module_body)
all_names = item_names + weapon_names
unique_names = {RARITY_RE.sub("", name) for name in all_names}

try:
    with open(JSON_PATH, encoding="utf-8") as f:
        binds = json.load(f)
except FileNotFoundError:
    binds = {}

new = unique_names - binds.keys()

with open(JSON_PATH, "w", encoding="utf-8") as f:
    json.dump(binds | dict.fromkeys(sorted(new)), f, indent=2)