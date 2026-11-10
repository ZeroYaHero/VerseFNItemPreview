import type { Disposable, OutputChannel } from 'vscode';
import { ChildProcessWithoutNullStreams, spawn } from 'node:child_process';
import { randomUUID } from 'node:crypto';
import { readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { LocalItem } from './matching';

interface BackendOptions {
    executable: string;
    paksDirectory: string;
    scanFile: string;
    cacheDirectory: string;
}

interface BackendResponse {
    filePath?: string;
    error?: string;
}

export class BackendClient implements Disposable {
    private process?: ChildProcessWithoutNullStreams;
    private readonly lifetime = new AbortController();
    private readonly token = randomUUID();
    private readonly previews = new Map<string, Promise<string>>();
    private endpoint?: string;
    private disposed = false;

    constructor(private readonly options: BackendOptions, private readonly output: OutputChannel) {}

    async start(): Promise<LocalItem[]> {
        const { executable, paksDirectory, scanFile, cacheDirectory } = this.options;
        const endpointFile = join(tmpdir(), `verse-preview-${randomUUID()}.txt`);
        this.output.appendLine(`Starting: ${executable}`);
        this.process = spawn(executable, [
            '--serve', '--paks', paksDirectory,
            '--out', scanFile, '--cache-dir', cacheDirectory
        ], {
            windowsHide: true,
            env: { ...process.env, VERSE_PREVIEW_ENDPOINT_FILE: endpointFile, VERSE_PREVIEW_TOKEN: this.token }
        });

        this.process.stdout.on('data', (data: Buffer) => this.output.append(data.toString()));
        this.process.stderr.on('data', (data: Buffer) => this.output.append(data.toString()));
        this.process.on('error', error => this.fail(error));
        this.process.on('exit', code => this.fail(new Error(`Backend exited (${code})`)));

        try {
            // The backend publishes its address only after scanning and starting HTTP.
            while (true) {
                this.lifetime.signal.throwIfAborted();
                let address = '';
                try {
                    address = await readFile(endpointFile, 'utf8');
                } catch (error) {
                    if ((error as NodeJS.ErrnoException).code !== 'ENOENT') {
                        throw error;
                    }
                }
                if (address) {
                    const url = new URL(address);
                    if (url.protocol !== 'http:' || url.hostname !== '127.0.0.1' || !url.port) {
                        throw new Error('Invalid backend address');
                    }
                    const report: { items: LocalItem[] } = JSON.parse(await readFile(scanFile, 'utf8'));
                    this.lifetime.signal.throwIfAborted();
                    this.endpoint = url.origin;
                    return report.items;
                }
                await delay(100, undefined, { signal: this.lifetime.signal });
            }
        } catch (error) {
            const failure = this.lifetime.signal.reason ?? error;
            this.fail(failure);
            this.process.kill();
            throw failure;
        } finally {
            await rm(endpointFile, { force: true });
        }
    }

    getPreview(iconPath: string): Promise<string> {
        const cached = this.previews.get(iconPath);
        if (cached) {
            return cached;
        }

        const request = this.requestPreview(iconPath).catch(error => {
            this.previews.delete(iconPath);
            throw error;
        });
        this.previews.set(iconPath, request);
        return request;
    }

    private async requestPreview(iconPath: string): Promise<string> {
        if (!this.endpoint || this.lifetime.signal.aborted) {
            throw new Error('Backend is not ready');
        }

        const signal = AbortSignal.any([this.lifetime.signal, AbortSignal.timeout(30000)]);
        try {
            const response = await fetch(`${this.endpoint}/preview`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${this.token}` },
                body: JSON.stringify({ iconPath }),
                signal
            });
            const result = await response.json().catch(() => ({})) as BackendResponse;
            if (!response.ok || result.error || !result.filePath) {
                throw new Error(result.error ?? `Preview request failed (${response.status})`);
            }
            return result.filePath;
        } catch (error) {
            if (this.lifetime.signal.aborted) {
                throw this.lifetime.signal.reason;
            }
            if (signal.aborted) {
                throw new Error('Preview export timed out');
            }
            throw error;
        }
    }

    private fail(error: Error): void {
        if (this.lifetime.signal.aborted) {
            return;
        }
        this.endpoint = undefined;
        this.lifetime.abort(error);
        this.previews.clear();
        if (!this.disposed) {
            this.output.appendLine(error.message);
        }
    }

    dispose(): void {
        this.disposed = true;
        this.fail(new Error('Extension deactivated'));
        this.process?.kill();
    }
}
