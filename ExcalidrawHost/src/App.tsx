/* eslint-disable @typescript-eslint/no-explicit-any */
import { Excalidraw } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI, UIOptions } from '@excalidraw/excalidraw/types/types';

let excalidrawApi: ExcalidrawImperativeAPI | null = null;

interface Interop {
    loadScene: (data: any) => void;
    getScene: () => any;
}

(window as any).interop = {
    excalidrawApi: null,
    loadScene: function (sceneData: any): void {
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

    return (
        <div style={{ width: '100vw', height: '100vh' }}>
            <Excalidraw
                excalidrawAPI={(api) => { excalidrawApi = api; }}
                UIOptions={uiOptions}
                onChange={ () => { (window as any).chrome.webview.postMessage({ event: 'onChange' }); } }
                />
        </div>
    )
}

export default App;