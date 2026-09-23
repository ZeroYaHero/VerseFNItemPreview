import * as vscode from 'vscode';
import itemBinds from '../../data/item_image_binds.json';

const RARITY_RE = /.*(_(Common|Uncommon|Rare|Epic|Legendary|Mythic|Exotic))/;

const RARITY_COLORS = {
	common: '#a0a0a0',
	uncommon: '#00c100',
	rare: '#0088ff',
	epic: '#a600ff',
	legendary: '#ffae00',
	mythic: '#fffc53',
	exotic: '#00fff7'
} as const;

function getRarityColor(rarity: string): string {
	return RARITY_COLORS[rarity as keyof typeof RARITY_COLORS];
}

function getPreviewImageMarkdown(imageUrl: string, width: number = 200, bg: string = '#fb0a0a'): vscode.MarkdownString {
	const md = new vscode.MarkdownString(`<span style="background-color:${bg};"><img src="${imageUrl}" width="${width}" height="${width}"/></span>`);
	md.supportHtml = true;
	return md;
}

export function activate(context: vscode.ExtensionContext) {
	const items: Record<string, string | null> = itemBinds.items;
	const verseHover = vscode.languages.registerHoverProvider('verse', {
		provideHover(document, position, token) {
			const range = document.getWordRangeAtPosition(position);
			if (!range) return;

			let word = document.getText(range);
			let color = '#00000000';

			const rarityResult = word.match(RARITY_RE);
			if (rarityResult) {
				color = getRarityColor(rarityResult[2].toLowerCase());
				word = word.substring(0, word.length - rarityResult[1].length)
			}

			if (!items[word]) {
				console.log("No image found for item:", word);
				return;
			}
			const imgUri = vscode.Uri.parse(`${itemBinds.base}${items[word]}`);
			const md = getPreviewImageMarkdown(imgUri.toString(), 200, color);
			return new vscode.Hover(md, range);
		}
	});
	context.subscriptions.push(verseHover);
}

// This method is called when your extension is deactivated
export function deactivate() {}
