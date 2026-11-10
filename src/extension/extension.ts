import * as vscode from 'vscode';
import itemBinds from '../../data/item_image_binds.json';
import { BackendClient } from './backendClient';
import { ItemHoverProvider } from './itemHoverProvider';

export async function activate(context: vscode.ExtensionContext): Promise<void> {
    await vscode.workspace.fs.createDirectory(context.globalStorageUri);

    const output = vscode.window.createOutputChannel('VerseFNItemPreview');
    const settings = vscode.workspace.getConfiguration('versefnitempreview');
    const backend = new BackendClient({
        executable: context.asAbsolutePath('out/backend/publish/Backend.exe'),
        paksDirectory: settings.get<string>('paksDirectory')!,
        scanFile: vscode.Uri.joinPath(context.globalStorageUri, 'items.json').fsPath,
        cacheDirectory: vscode.Uri.joinPath(context.globalStorageUri, 'cache').fsPath
    }, output);

    const knownTokens = new Set(Object.keys(itemBinds.items)); // TODO: As mentioned in the README.md this is just an artifact of the previous method. Worth considering a different way.
    const hoverProvider = new ItemHoverProvider(backend, knownTokens, output);
    const registration = vscode.languages.registerHoverProvider('verse', hoverProvider);
    context.subscriptions.push(registration, backend, output);

    backend.start()
        .then(items => hoverProvider.setItems(items))
        .catch(error => output.appendLine(`Startup failed: ${String(error)}`));
}
