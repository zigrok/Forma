import { dotnet } from './_framework/dotnet.js';
const status = document.querySelector('#status');
try {
    const runtime = await dotnet.withModuleConfig({canvas: document.querySelector('#canvas')}).create();
    await runtime.runMain();
    const exports = await runtime.getAssemblyExports(runtime.getConfig().mainAssemblyName);
    const textResult = await exports.BrowserProbe.Run(new URL('./', location.href).href);
    const graphicsResult = await new Promise((resolve, reject) => {
        const frame = () => {
            try {
                const running = exports.BrowserProbe.Tick();
                const result = exports.BrowserProbe.GraphicsResult();
                if (result) resolve(result);
                else if (!running) reject(new Error('Native game exited before the graphics proof completed.'));
                else requestAnimationFrame(frame);
            } catch (error) { reject(error); }
        };
        requestAnimationFrame(frame);
    });
    status.textContent = textResult + '\n' + graphicsResult;
    status.dataset.state = 'passed';
} catch (error) {
    status.textContent = String(error?.stack ?? error);
    status.dataset.state = 'failed';
    console.error(error);
}
