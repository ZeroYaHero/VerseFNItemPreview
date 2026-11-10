import { distance } from 'fastest-levenshtein';

export interface LocalItem {
    displayName: string;
    iconPath: string;
    largeIconPath?: string;
    gameplayTags?: string[];
    sources: { assetName: string; objectPath: string }[];
}

export type ItemIndex = Map<string, LocalItem[]>;
const similarityCutoff = 0.85;

export function createItemIndex(items: LocalItem[]): ItemIndex {
    const index: ItemIndex = new Map();
    for (const item of items) {
        if (!item.iconPath) {
            continue;
        }
        // Functionally, this finds the first of a displayName and sets duplicate as sources.
        const name = normalize(item.displayName);
        const candidates = index.get(name) ?? [];
        candidates.push(item);
        index.set(name, candidates);
    }
    return index;
}

export function matchItem(token: string, index: ItemIndex): LocalItem | undefined {
    const name = normalize(token.split('_')[0]);
    let candidates = index.get(name);

    if (!candidates) {
        const closestName = findClosestName(name, index);
        if (closestName === undefined) {
            return undefined;
        }
        candidates = index.get(closestName) ?? [];
    }

    // This ideally restricts the candidates to the same season in case of name collision (beneficial if they have different models/icons)
    const season = token.match(/CH\d+(?:MS|S)\d+/i)?.[0].toLowerCase();
    if (season) {
        const sameSeason = candidates.filter(item => getSeasons(item).includes(season));
        if (sameSeason.length > 0) {
            candidates = sameSeason;
        }
    }

    return candidates[0];
}

function findClosestName(name: string, index: ItemIndex): string | undefined {
    let closestName: string | undefined;
    let closestScore = 0;
    for (const candidate of index.keys()) { // Reminder that the index keys is the normalized display name! (and so is the parameter 'name' in this function)
        const longestLength = Math.max(name.length, candidate.length);
        if (longestLength === 0) {
            continue;
        }
        const score = 1 - distance(name, candidate) / longestLength;
        if (score >= similarityCutoff && score > closestScore) {
            closestName = candidate;
            closestScore = score;
        }
    }
    return closestName;
}

function normalize(name: string): string {
    return name.replace(/\([^)]*\)/g, '').toLowerCase().replace(/[^a-z0-9]/g, '');
}

function getSeasons(item: LocalItem): string[] {
    const fields = [item.displayName, item.iconPath, item.largeIconPath ?? '', ...(item.gameplayTags ?? [])];
    for (const source of item.sources) {
        fields.push(source.assetName, source.objectPath);
    }
    const seasons = fields.join(' ').match(/CH\d+(?:MS|S)\d+/ig) ?? []; // Simple CHXSY regex
    return seasons.map(season => season.toLowerCase());
}
