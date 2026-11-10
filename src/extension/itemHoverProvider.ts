import * as vscode from 'vscode';
import { BackendClient } from './backendClient';
import { createItemIndex, ItemIndex, LocalItem, matchItem } from './matching';
import { parseToken } from './verseToken';

export class ItemHoverProvider implements vscode.HoverProvider {
    private index?: ItemIndex;
    private readonly matches = new Map<string, LocalItem | undefined>();

    constructor(
        private readonly backend: BackendClient,
        private readonly knownTokens: ReadonlySet<string>,
        private readonly output: vscode.OutputChannel
    ) {}

    setItems(items: LocalItem[]): void {
        this.index = createItemIndex(items);
        this.matches.clear();
        this.output.appendLine(`Ready: ${items.length} image groups`);
    }

    async provideHover(
        document: vscode.TextDocument,
        position: vscode.Position,
        cancellation: vscode.CancellationToken
    ): Promise<vscode.Hover | undefined> {
        if (!this.index || cancellation.isCancellationRequested) {
            return undefined;
        }

        const range = document.getWordRangeAtPosition(position, /[A-Za-z_][A-Za-z0-9_]*/);
        if (!range) {
            return undefined;
        }

        const token = parseToken(document.getText(range));
        if (!this.knownTokens.has(token.key)) {
            return undefined;
        }

        const item = this.findMatch(token.key);
        if (!item) {
            return undefined;
        }

        try {
            const filePath = await this.backend.getPreview(item.iconPath);
            if (cancellation.isCancellationRequested) {
                return undefined;
            }

            const markdown = createPreviewMarkdown(filePath, token.color);
            return new vscode.Hover(markdown, range);
        } catch (error) {
            this.output.appendLine(`Preview ${token.key}: ${String(error)}`);
            return undefined;
        }
    }

    private findMatch(key: string): LocalItem | undefined {
        if (!this.index) {
            return undefined;
        }
        if (!this.matches.has(key)) {
            const item = matchItem(key, this.index);
            this.matches.set(key, item);
            if (!item) {
                this.output.appendLine(`No local match: ${key}`);
            }
        }
        return this.matches.get(key);
    }
}

function createPreviewMarkdown(filePath: string, color: string): vscode.MarkdownString {
    const imageUri = vscode.Uri.file(filePath).toString();
    let content = `<img src="${imageUri}" width="200"/>`;
    if (color !== '#00000000') {
        const bar = '&#x2501;'.repeat(20);
        content += `\n\n<span style="color:${color};">${bar}</span>`;
    }

    const markdown = new vscode.MarkdownString(content);
    markdown.supportHtml = true;
    return markdown;
}
