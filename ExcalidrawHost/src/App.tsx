/* eslint-disable @typescript-eslint/no-explicit-any */
import { Excalidraw, exportToBlob, loadFromBlob } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI, LibraryItems, UIOptions } from '@excalidraw/excalidraw/types';
import { ExcalidrawElement, Theme } from '@excalidraw/excalidraw/element/types';
import '@excalidraw/excalidraw/index.css';

let excalidrawApi: ExcalidrawImperativeAPI | null = null;
let currentVersionSum: number = 0;
let isLibraryLoading: boolean = false;
let libraryItemsRef: LibraryItems = [];

const theme = (document.getElementById("root")?.getAttribute('data-theme') as Theme) || "light"; // Default to 'light' theme

function calculateElementVersionSum(elements: readonly ExcalidrawElement[]): number {
    return elements.reduce((acc: number, e: ExcalidrawElement) => acc + e.version, 0);
}

(window as any).interop = {
    loadSceneAsync: async function (sceneData: any, contentType: string): Promise<void> {
        let scene: any;
        if (contentType == 'image/png') {
            const sceneBlob = new Blob([new Uint8Array(sceneData)], { type: 'image/png' });
            scene = await loadFromBlob(sceneBlob, null, null);
        } else if (contentType == 'application/json') {
            scene = sceneData;
        }

        scene.appState.theme = theme;

        // Update the current version sum so we don't raise onChange event back to the host.
        currentVersionSum = calculateElementVersionSum(scene.elements);
        excalidrawApi!.updateScene(scene);
        const filesArray: BinaryFileData[] = Object.values(scene.files);
        excalidrawApi!.addFiles(filesArray);
    },
    loadLibrary: function (libraryItems: LibraryItems): void {
        isLibraryLoading = true;
        excalidrawApi!.updateLibrary({ libraryItems });
    },
    saveSceneAsync: async function (contentType: string): Promise<void> {
        const elements = excalidrawApi!.getSceneElements();
        const appStateCurrent = excalidrawApi!.getAppState();
        const appState = {
            currentItemStrokeColor: appStateCurrent.currentItemStrokeColor,
            currentItemBackgroundColor: appStateCurrent.currentItemBackgroundColor,
            currentItemFillStyle: appStateCurrent.currentItemFillStyle,
            currentItemStrokeWidth: appStateCurrent.currentItemStrokeWidth,
            currentItemStrokeStyle: appStateCurrent.currentItemStrokeStyle,
            currentItemRoughness: appStateCurrent.currentItemRoughness,
            currentItemOpacity: appStateCurrent.currentItemOpacity,
            currentItemFontFamily: appStateCurrent.currentItemFontFamily,
            currentItemFontSize: appStateCurrent.currentItemFontSize,
            currentItemTextAlign: appStateCurrent.currentItemTextAlign,
            currentItemStartArrowhead: appStateCurrent.currentItemStartArrowhead,
            currentItemEndArrowhead: appStateCurrent.currentItemEndArrowhead,
            scrollX: appStateCurrent.scrollX,
            scrollY: appStateCurrent.scrollY,
            zoom: appStateCurrent.zoom,
            currentItemRoundness: appStateCurrent.currentItemRoundness,
            gridSize: appStateCurrent.gridSize,
            frameRendering: appStateCurrent.frameRendering,
            objectsSnapModeEnabled: appStateCurrent.objectsSnapModeEnabled,
        };
        const files = { ...excalidrawApi!.getFiles() };

        if (files) {
            const imageIds = elements.filter((e : any) => e.type === "image").map((e : any) => e.fileId);
            const toDelete = Object.keys(files).filter((k) => !imageIds.includes(k));
            toDelete.forEach((k) => delete files[k]);
        }

        if (contentType == 'image/png') {
            const blob = await exportToBlob({
                elements,
                appState: {
                    ...appState,
                    exportBackground: true,
                    exportEmbedScene: true
                },
                files
            });
            const blobArray = Array.from(new Uint8Array(await blob.arrayBuffer()));
            const message = {
                event: 'onSceneSave',
                contentType,
                data: blobArray
            };
            (window as any).chrome.webview.postMessage(message);

        } else if (contentType == 'application/json') {
            const sceneData = {
                type: "excalidraw",
                version: 2,
                source: "https://philiphendry.me.uk/excalidrawinvisualstudio",
                elements,
                appState,
                files
            };
            const message = {
                event: 'onSceneSave',
                contentType,
                data: Array.from(new TextEncoder().encode(JSON.stringify(sceneData)))
            };
            (window as any).chrome.webview.postMessage(message);
        }
    },
    setTheme: function (theme: Theme) : void {
        // Update the theme prop on Excalidraw
        excalidrawApi!.updateScene({
            appState: {
                theme: theme
            }
        });
    }
};

if ((window as any).chrome.webview === undefined) {
    (window as any).chrome.webview = {
        postMessage: (json: any) => {
            console.warn("Message posted to WebView host: ", json)
        }
    };
}

function App() {
    const uiOptions: UIOptions = {
        canvasActions: {
            loadScene: false,
            saveToActiveFile: false
        }
    };

    let cancelTimeoutId: number = 0;

    // Report change events back to the host but debouncing them so we don't spam the host
    // and using the sum of version numbers across elements to detect changes as otherwise
    // moving the mouse causes changes events to be fired even when nothing has changed.
    function handleOnChangeEvent(elements: readonly ExcalidrawElement[]) {
        if (cancelTimeoutId) {
            clearTimeout(cancelTimeoutId);
        }
        cancelTimeoutId = setTimeout(() => {
            const versionSum: number = calculateElementVersionSum(elements);
            if (versionSum === currentVersionSum) {
                return;
            }
            currentVersionSum = versionSum;
            (window as any).chrome.webview.postMessage({ event: 'onChange', versionSum: versionSum });
        }, 100);
    }

    function handleOnLibraryChange(libraryItems: LibraryItems) {
        if (JSON.stringify(libraryItemsRef) === JSON.stringify(libraryItems)) {
            return;
        }
        libraryItemsRef = libraryItems;
        if (isLibraryLoading) {
            isLibraryLoading = false;
            return;
        }
        (window as any).chrome.webview.postMessage({ event: 'onLibraryChange', libraryItems: libraryItems });
    }

    return (
        <div style={{ width: '100vw', height: '100vh' }}>
            <Excalidraw
                excalidrawAPI={(api) => {
                    excalidrawApi = api;
                        (window as any).chrome.webview.postMessage({ event: 'onReady' });
                }}
                theme={theme as Theme}
                UIOptions={uiOptions}
                onChange={handleOnChangeEvent}
                onLibraryChange={handleOnLibraryChange}
            />
        </div>
    )
}

export default App;
