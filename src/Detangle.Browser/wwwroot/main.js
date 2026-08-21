// The demo's whole host page: start the .NET runtime, hand it the canvas, remove the
// loading panel. There is no framework here and nothing is fetched from anywhere but
// this origin — which is the claim the demo exists to make.
import { dotnet } from './_framework/dotnet.js';

const app = await dotnet.create();
const config = app.getConfig();

document.getElementById('loading')?.remove();

await app.runMain(config.mainAssemblyName, []);
