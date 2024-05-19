import { useState } from 'react';
import { Excalidraw } from '@excalidraw/excalidraw';
import { BinaryFileData, ExcalidrawImperativeAPI } from '@excalidraw/excalidraw/types/types';
import initialData from './initialData';
import './App.css'

function App() {
    const [excalidrawAPI, setExcalidrawAPI] = useState<ExcalidrawImperativeAPI | null>(null);

    function loadScene() {
        console.log('loadScene');
        const fetchData = async () => {
            try {
                const response = await fetch('example.excalidraw');
                const data = await response.json();
                excalidrawAPI!.updateScene(data);
                const filesArray: BinaryFileData[] = Object.values(data.files);
                excalidrawAPI!.addFiles(filesArray);
            } catch (error) {
                console.error('Error fetching JSON:', error);
            }
        };
        fetchData();
    }

    return (
        <>
            <h1>Excalidraw Examples</h1>
            <div style={{ height: '300px' }}>
                <Excalidraw initialData={initialData} />
            </div>
            <div style={{ height: '300px' }}>
                <Excalidraw
                    excalidrawAPI={(api) => { setExcalidrawAPI(api); }} />
            </div>
            <button onClick={loadScene}>Load Scene</button>
        </>
    )
}

export default App
