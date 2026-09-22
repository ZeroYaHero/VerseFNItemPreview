// The module 'vscode' contains the VS Code extensibility API
// Import the module and reference it with the alias vscode in your code below
import * as vscode from 'vscode';
import { Hover } from 'vscode';

function getPreviewImageMarkdown(imageUrl: string, width = 200, bg = '#fb0a0a'): vscode.MarkdownString {
  const md = new vscode.MarkdownString(
    `<span style="background-color:${bg};"><img src="${imageUrl}" width="${width}"/></span>`
  );
  md.supportHtml = true;
  return md;
}

// This method is called when your extension is activated
// Your extension is activated the very first time the command is executed
export function activate(context: vscode.ExtensionContext) {

	// Use the console to output diagnostic information (console.log) and errors (console.error)
	// This line of code will only be executed once when your extension is activated
	console.log('Congratulations, your extension "versefnitempreview" is now active!');

	// The command has been defined in the package.json file
	// Now provide the implementation of the command with registerCommand
	// The commandId parameter must match the command field in package.json
	const disposable = vscode.commands.registerCommand('versefnitempreview.helloWorld', () => {
		// The code you place here will be executed every time your command is executed
		// Display a message box to the user
		vscode.window.showInformationMessage('Hello World from VerseFNItemPreview!');
	});
	context.subscriptions.push(disposable);

	console.log("VerseFNItemPreview extension activated.");

	// Here 
	const verseHover = vscode.languages.registerHoverProvider('verse', {
		provideHover(document, position, token) {
			const range = document.getWordRangeAtPosition(position);
			if (!range) return;

			const word = document.getText(range);
			// if (word !== 'myToken') return; // your matching logic

			// const imgUri = vscode.Uri.joinPath(context.extensionUri, 'images', 'preview.png');
			const imgUri = vscode.Uri.parse(`https://fnggcdn.com/assets-s/af47tb.jpg`);
			// const md = new vscode.MarkdownString(`![preview](${imgUri.toString()}|width=200)`);
			const md = getPreviewImageMarkdown(imgUri.toString(), 200);
			return new vscode.Hover(md, range);
			// return new vscode.Hover("Hello this is a verse test preview");
			// return new Hover(new vscode.MarkdownString(`![img](https://fnggcdn.com/assets-s/af47tb.jpg)`));
		}
	});
	context.subscriptions.push(verseHover);
	
	console.log('Hover provider registered for Verse language.');
}

// This method is called when your extension is deactivated
export function deactivate() {}
