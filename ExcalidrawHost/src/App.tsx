/* eslint-disable @typescript-eslint/no-explicit-any */
import { Excalidraw } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI, UIOptions } from '@excalidraw/excalidraw/types/types';
import { ExcalidrawElement, Theme } from '@excalidraw/excalidraw/types/element/types';

let excalidrawApi: ExcalidrawImperativeAPI | null = null;
let currentVersionSum: number = 0;

interface Interop {
    loadScene: (data: any) => void;
    getScene: () => any;
}

function calculateElementVersionSum(elements: readonly ExcalidrawElement[]): number {
    return elements.reduce((acc: number, e: ExcalidrawElement) => acc + e.version, 0);
}

(window as any).interop = {
    loadScene: function (sceneData: any): void {
        // Update the current version sum so we don't raise onChange event back to the host.
        currentVersionSum = calculateElementVersionSum(sceneData.elements);
        excalidrawApi!.updateScene(sceneData);
        const filesArray: BinaryFileData[] = Object.values(sceneData.files);
        excalidrawApi!.addFiles(filesArray);
    },
    getScene: function () {
        const elements = excalidrawApi!.getSceneElements();
        const appState = excalidrawApi!.getAppState();
        const files = { ...excalidrawApi!.getFiles() };
        if (files) {
            const imageIds = elements.filter((e : any) => e.type === "image").map((e : any) => e.fileId);
            const toDelete = Object.keys(files).filter((k) => !imageIds.includes(k));
            toDelete.forEach((k) => delete files[k]);
        }
        return {
            type: "excalidraw",
            version: 2,
            source: "https://philiphendry.me.uk/excalidrawinvisualstudio",
            elements,
            appState: {
                theme: appState.theme,
                viewBackgroundColor: appState.viewBackgroundColor,
                currentItemStrokeColor: appState.currentItemStrokeColor,
                currentItemBackgroundColor: appState.currentItemBackgroundColor,
                currentItemFillStyle: appState.currentItemFillStyle,
                currentItemStrokeWidth: appState.currentItemStrokeWidth,
                currentItemStrokeStyle: appState.currentItemStrokeStyle,
                currentItemRoughness: appState.currentItemRoughness,
                currentItemOpacity: appState.currentItemOpacity,
                currentItemFontFamily: appState.currentItemFontFamily,
                currentItemFontSize: appState.currentItemFontSize,
                currentItemTextAlign: appState.currentItemTextAlign,
                currentItemStartArrowhead: appState.currentItemStartArrowhead,
                currentItemEndArrowhead: appState.currentItemEndArrowhead,
                scrollX: appState.scrollX,
                scrollY: appState.scrollY,
                zoom: appState.zoom,
                currentItemRoundness: appState.currentItemRoundness,
                gridSize: appState.gridSize,
                frameRendering: appState.frameRendering,
                objectsSnapModeEnabled: appState.objectsSnapModeEnabled,
            },
            files
        };
    },
    setTheme: function (theme: Theme) {
        // Update the theme prop on Excalidraw
        excalidrawApi!.updateScene({
            appState: {
                theme: theme
            }
        });
    }
} as Interop;

if ((window as any).chrome.webview === undefined) {
    (window as any).chrome.webview = {
        postMessage: (json: any) => {
            console.warn("Message posted to WebView host: ", json)
        }
    };
}

function App() {
    const uiOptions : UIOptions = {
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

    const theme = document.getElementById("root")?.getAttribute('data-theme');

    return (
        <div style={{ width: '100vw', height: '100vh' }}>
            <Excalidraw
                excalidrawAPI={(api) => {
                    excalidrawApi = api;
                    (window as any).chrome.webview.postMessage({ event: 'onReady' });
                }}
                theme={theme as Theme}
                UIOptions={uiOptions}
                onChange={ handleOnChangeEvent }
                />
        </div>
    )
}

export default App;
