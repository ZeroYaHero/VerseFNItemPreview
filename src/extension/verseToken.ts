export const rarityColors = {
    common: '#a0a0a0',
    uncommon: '#00c100',
    rare: '#0088ff',
    epic: '#a600ff',
    legendary: '#ffae00',
    mythic: '#fffc53',
    exotic: '#00fff7'
} as const;

export interface VerseToken {
    key: string;
    color: string;
}

export function parseToken(word: string): VerseToken {
    const rarity = word.match(/_(Common|Uncommon|Rare|Epic|Legendary|Mythic|Exotic)(?=_|$)/i);
    if (!rarity) {
        return { key: word, color: '#00000000' };
    }

    const rarityName = rarity[1].toLowerCase() as keyof typeof rarityColors;
    return {
        key: word.replace(rarity[0], ''),
        color: rarityColors[rarityName]
    };
}
