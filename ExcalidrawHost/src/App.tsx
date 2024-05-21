/* eslint-disable @typescript-eslint/no-explicit-any */
import { useState, useEffect } from 'react';
import { Excalidraw } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI, UIOptions } from '@excalidraw/excalidraw/types/types';

(window as any).interop = {
    load: function (data: any): void {
        const loadEvent = new CustomEvent('loadScene', { detail: data });
        window.dispatchEvent(loadEvent);
    },
    getSceneAsync: async function () {
        const promise = new Promise<any>((resolve, reject) => {
            const getSceneEvent = new CustomEvent('getScene', { detail: { resolve, reject } });
            window.dispatchEvent(getSceneEvent);
        });
        const sceneData : any = await promise;
        return sceneData;
    }
};

function App() {
    const [excalidrawAPI, setExcalidrawAPI] = useState<ExcalidrawImperativeAPI>();

    useEffect(() => {
        function handleLoadScene(event: CustomEvent<any>): void {
            const sceneData = event.detail;
            excalidrawAPI!.updateScene(sceneData);
            const filesArray: BinaryFileData[] = Object.values(sceneData.files);
            excalidrawAPI!.addFiles(filesArray);
        }

        function handleGetScene(event: CustomEvent<any>): void {
            const { resolve, reject } = event.detail;
            try {
                const sceneElements = excalidrawAPI!.getSceneElements();
                resolve(sceneElements);
            } catch (error) {
                reject(error);
            }
        }

        window.addEventListener('loadScene', handleLoadScene as EventListener);
        window.addEventListener('getScene', handleGetScene as EventListener);
        return () => {
            window.removeEventListener('loadScene', handleLoadScene as EventListener);
            window.removeEventListener('getScene', handleGetScene as EventListener);
        };
    }, [excalidrawAPI]);

    const uiOptions : UIOptions = {
        canvasActions: {
            loadScene: false,
            saveToActiveFile: false
        }
    };

    return (
        <div style={{ width: '100vw', height: '100vh' }}>
            <Excalidraw
                excalidrawAPI={(api) => { setExcalidrawAPI(api); }}
                UIOptions={uiOptions}
                />
        </div>
    )
}

export default App;