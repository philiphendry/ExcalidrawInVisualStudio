/* eslint-disable @typescript-eslint/no-explicit-any */
import { useState, useEffect } from 'react';
import { Excalidraw } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types/types';


(window as any).interop = {
    load: function (data: any): void {
        const loadEvent = new CustomEvent('loadScene', { detail: data });
        window.dispatchEvent(loadEvent);
    },
};

function App() {
    const [excalidrawAPI, setExcalidrawAPI] = useState<ExcalidrawImperativeAPI>();

    useEffect(() => {
        const handleLoadScene = (event: CustomEvent<any>): void => loadSceneFromHost(event.detail);
        window.addEventListener('loadScene', handleLoadScene as EventListener);
        return () => {
            window.removeEventListener('loadScene', handleLoadScene as EventListener);
        };
    }, [excalidrawAPI]);

    function loadSceneFromHost(data: any) {
        excalidrawAPI!.updateScene(data);
        const filesArray: BinaryFileData[] = Object.values(data.files);
        excalidrawAPI!.addFiles(filesArray);
    }

    return (
        <>
            <div style={{ width: '100vw', height: '100vh' }}>
                <Excalidraw
                    excalidrawAPI={(api) => { setExcalidrawAPI(api); }} />
            </div>
        </>
    )
}

export default App;