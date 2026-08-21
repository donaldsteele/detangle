// The demo's whole host page: start the .NET runtime, hand it the canvas, remove the
// loading panel. There is no framework here and nothing is fetched from anywhere but
// this origin — which is the claim the demo exists to make.
import { dotnet } from './_framework/dotnet.js';

const app = await dotnet.create();
const config = app.getConfig();

document.getElementById('loading')?.remove();

// "?page=wiki/schema" opens the demo on that page, so the website can link straight to
// the diagram it is talking about. The value is handed over as a command-line argument
// rather than through interop: the runtime already has a way to pass one.
const page = new URLSearchParams(location.search).get('page');

await app.runMain(config.mainAssemblyName, page ? [page] : []);
