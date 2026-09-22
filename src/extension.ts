import * as vscode from 'vscode';

function getPreviewImageMarkdown(imageUrl: string, width = 200, bg = '#fb0a0a'): vscode.MarkdownString {
  const md = new vscode.MarkdownString(
    `<span style="background-color:${bg};"><img src="${imageUrl}" width="${width}"/></span>`
  );
  md.supportHtml = true;
  return md;
}

// TODO
// function getRarityColor(rarity: string): string

export function activate(context: vscode.ExtensionContext) {
	console.log("VerseFNItemPreview extension activated.");

	// Here 
	const verseHover = vscode.languages.registerHoverProvider('verse', {
		provideHover(document, position, token) {
			const range = document.getWordRangeAtPosition(position);
			if (!range) return;

			const word = document.getText(range);
			// TODO: Matching logic

			const imgUri = vscode.Uri.parse(`https://fnggcdn.com/assets-s/af47tb.jpg`);
			const md = getPreviewImageMarkdown(imgUri.toString(), 200);
			return new vscode.Hover(md, range);
		}
	});
	context.subscriptions.push(verseHover);
}

// This method is called when your extension is deactivated
export function deactivate() {}
